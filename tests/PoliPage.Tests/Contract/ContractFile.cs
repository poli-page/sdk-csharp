using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoliPage.Tests.Contract;

/// <summary>
/// The pinned API contract (<c>contract.json</c> of poli-page/api-contract), read from the
/// path in <c>POLI_CONTRACT</c>, and the repository's skip list.
/// </summary>
internal sealed class ContractFile
{
    /// <summary>The contract.json layout this harness reads.</summary>
    public const int FormatVersion = 1;

    /// <summary>This SDK's name in <c>policy.cancellationSdks</c>.</summary>
    public const string SdkName = "csharp";

    public const string SkipFileName = "contract-skip.json";

    /// <summary>The <c>policy.normalizations</c> this harness applies; each must still be listed.</summary>
    public static readonly IReadOnlyList<string> AppliedNormalizations =
    [
        "N-STATUS-NULL",
        "N-REQID-NULL",
        "N-BYTES",
        "N-DELETE-NULL",
        "N-THUMBS-ARRAY",
        "N-PREVIEW",
        "N-DESCRIPTOR",
        "N-DURATION-MS",
    ];

    private static readonly Lazy<ContractFile> Shared = new(Load);

    private ContractFile(
        string? unavailableReason,
        IReadOnlyList<string> problems,
        IReadOnlyDictionary<string, JsonObject> sdkCases,
        IReadOnlyList<string> runnableIds,
        IReadOnlyDictionary<string, string> skipList,
        IReadOnlySet<string> cancellationSdks)
    {
        UnavailableReason = unavailableReason;
        Problems = problems;
        SdkCases = sdkCases;
        RunnableIds = runnableIds;
        SkipList = skipList;
        CancellationSdks = cancellationSdks;
    }

    public static ContractFile Current => Shared.Value;

    /// <summary>Set when the contract cannot be run: why.</summary>
    public string? UnavailableReason { get; }

    /// <summary>What is wrong with the contract file or the skip list.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Every <c>kind: "sdk"</c> case, by id.</summary>
    public IReadOnlyDictionary<string, JsonObject> SdkCases { get; }

    /// <summary>The ids of the sdk cases this SDK runs, in contract order.</summary>
    public IReadOnlyList<string> RunnableIds { get; }

    public IReadOnlyDictionary<string, string> SkipList { get; }

    public IReadOnlySet<string> CancellationSdks { get; }

    /// <summary>
    /// Local runs without <c>POLI_CONTRACT</c> skip the suite with this reason; in CI
    /// (<c>CI</c> set) a missing contract fails it instead.
    /// </summary>
    public static string? LocalSkipReason
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLI_CONTRACT"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            ? "API contract tests skipped: set POLI_CONTRACT to a contract.json path"
            : null;

    private static ContractFile Load()
    {
        var path = Environment.GetEnvironmentVariable("POLI_CONTRACT");
        if (string.IsNullOrEmpty(path))
        {
            return Unavailable(
                "POLI_CONTRACT is not set: in CI the \"API contract\" step must provide contract.json "
                + "(poli-page/api-contract action, output \"path\")");
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("not a JSON object");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable($"cannot read the contract at {path}: {ex.Message}");
        }

        if (root["formatVersion"] is not JsonValue version || !version.TryGetValue<int>(out var format))
            return Unavailable($"{path} is not a contract.json: it has no formatVersion");
        if (format != FormatVersion)
        {
            return Unavailable(
                $"contract.json formatVersion {format} is not supported (this harness reads format {FormatVersion}): "
                + "update tests/PoliPage.Tests/Contract");
        }

        Dictionary<string, string> skipList;
        try
        {
            skipList = LoadSkipList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            return Unavailable($"{SkipFileName}: {ex.Message}");
        }

        return FromContract(root, skipList);
    }

    private static ContractFile FromContract(JsonObject root, Dictionary<string, string> skipList)
    {
        var problems = new List<string>();
        var policy = root["policy"] as JsonObject;
        var cancellationSdks = new HashSet<string>(
            (policy?["cancellationSdks"] as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? ""),
            StringComparer.Ordinal);
        var normalizations = new HashSet<string>(
            (policy?["normalizations"] as JsonArray ?? []).Select(n => n?["id"]?.GetValue<string>() ?? ""),
            StringComparer.Ordinal);
        foreach (var id in AppliedNormalizations.Where(id => !normalizations.Contains(id)))
            problems.Add($"normalization {id} is applied here but no longer listed by the contract");
        foreach (var id in normalizations.Where(id => !AppliedNormalizations.Contains(id)))
            problems.Add($"the contract lists normalization {id}, which this harness does not apply: implement it");

        var sdkCases = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var runnable = new List<string>();
        var cases = root["cases"] as JsonArray ?? [];
        for (var i = 0; i < cases.Count; i++)
        {
            var item = cases[i] as JsonObject;
            var id = item?["id"]?.GetValue<string>();
            var kind = item?["kind"]?.GetValue<string>();
            switch (kind)
            {
                case "sdk" when id is not null:
                    if (!sdkCases.TryAdd(id, item!))
                        problems.Add($"case id {id} appears twice");
                    else if (item!["requires"]?.GetValue<string>() != "cancellation" || cancellationSdks.Contains(SdkName))
                        runnable.Add(id);
                    break;
                case "impl":
                    // For integration libraries.
                    break;
                default:
                    problems.Add($"cases[{i}] ({id}) has kind \"{kind}\": this harness knows only \"sdk\" and \"impl\"");
                    break;
            }
        }

        if (sdkCases.Count == 0)
            problems.Add("the contract has no \"sdk\" case");

        foreach (var id in skipList.Keys)
        {
            if (!sdkCases.ContainsKey(id))
                problems.Add($"{SkipFileName} lists \"{id}\", which the contract does not have: remove it");
            else if (!runnable.Contains(id))
                problems.Add($"{SkipFileName} lists \"{id}\", which this SDK does not run: remove it");
        }

        return new ContractFile(null, problems, sdkCases, runnable, skipList, cancellationSdks);
    }

    private static Dictionary<string, string> LoadSkipList()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SkipFileName);
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("must be an object of case id to reason");
        var skip = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, reason) in root)
        {
            var text = reason is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"\"{id}\" needs a reason");
            skip[id] = text;
        }

        return skip;
    }

    private static ContractFile Unavailable(string reason)
        => new(
            reason,
            [reason],
            new Dictionary<string, JsonObject>(StringComparer.Ordinal),
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
}
