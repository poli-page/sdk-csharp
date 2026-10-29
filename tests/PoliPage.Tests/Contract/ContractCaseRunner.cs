// The harness converts between JSON and the SDK's types with reflection-based
// System.Text.Json, as the other tests do; trimming and AOT do not apply to test code.
#pragma warning disable IL2026, IL3050
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PoliPage.Tests.Contract;

/// <summary>How the client under test reaches the mock.</summary>
internal enum ClientTransport
{
    /// <summary>The HttpClients the SDK creates itself.</summary>
    SdkOwned,

    /// <summary>
    /// One caller-supplied <see cref="HttpClient"/> (default handler, 100 s timeout) for both
    /// the API and the download, as <c>PoliPageClientOptions.HttpClient</c> and
    /// <c>DownloadHttpClient</c>: the SDK's timeout and its no-resend rule must hold through
    /// it, and the download reuses the API's keep-alive connection.
    /// </summary>
    Injected,
}

/// <summary>A case the harness cannot run as written (a contract or harness problem).</summary>
internal sealed class ContractCaseException(string message) : Exception(message);

/// <summary>
/// Runs one <c>kind: "sdk"</c> case: a local server answers the case's responses in order and
/// records the requests; a client built with the case's options calls the method; the
/// recorded requests, the <c>OnRequest</c> / <c>OnRetry</c> events and the normalized result
/// are compared with <c>expect</c>, after only the normalizations of
/// <c>policy.normalizations</c>. Timing is checked as lower bounds, measured on the server.
/// </summary>
internal static partial class ContractCaseRunner
{
    /// <summary>The client's key when a case sets no <c>apiKey</c>.</summary>
    private const string TestApiKey = "pp_sandbox_00000000000000000000000000000000";

    /// <summary>Bounds one case; the slowest (a 2 s Retry-After date) takes about 2 s.</summary>
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions ArgsOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<ThumbnailFormat>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<PageFormat>(),
        },
    };

    private static readonly string[] CaseFields =
        ["kind", "id", "title", "requires", "apiKey", "options", "call", "responses", "expect", "stopAfterRetryEvents"];

    private static readonly string[] OptionFields = ["maxRetries", "retryDelayMs", "timeoutMs"];
    private static readonly string[] CallFields = ["method", "args", "cancelAfterMs"];
    private static readonly string[] AnswerFields = ["status", "headers", "json", "text", "base64"];
    private static readonly string[] ExpectFields = ["requests", "events", "result"];
    private static readonly string[] BodyFields = ["json", "text", "base64"];

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidV4();

    /// <summary>Runs the case; returns what did not match (empty when the case passes).</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(JsonObject testCase, ClientTransport transport)
    {
        try
        {
            return await RunCheckedAsync(testCase, transport);
        }
        catch (Exception ex) when (ex is ContractCaseException or InvalidDataException or JsonException
            or InvalidOperationException or FormatException)
        {
            return [$"cannot run the case: {ex.Message}"];
        }
    }

    private static async Task<IReadOnlyList<string>> RunCheckedAsync(JsonObject testCase, ClientTransport transport)
    {
        CheckFields(testCase, CaseFields, "case");
        var options = Obj(testCase, "options");
        CheckFields(options, OptionFields, "options");
        var call = Obj(testCase, "call");
        CheckFields(call, CallFields, "call");
        var expect = Obj(testCase, "expect");
        CheckFields(expect, ExpectFields, "expect");
        var requires = testCase["requires"]?.GetValue<string>();
        if (requires is not null and not "cancellation")
            throw new ContractCaseException($"unknown case requirement \"{requires}\"");

        var apiKey = testCase["apiKey"]?.GetValue<string>() ?? TestApiKey;
        var method = call["method"]!.GetValue<string>();
        var cancelAfterMs = call["cancelAfterMs"]?.GetValue<int>();
        var stopAfterRetryEvents = testCase["stopAfterRetryEvents"]?.GetValue<int>();
        var responses = testCase["responses"] as JsonArray ?? throw new ContractCaseException("no responses");
        var (wantEvents, wantRequests, checkResult) = ExpectedPrefix(expect, stopAfterRetryEvents);

        var server = new MockServer(origin => BuildSteps(responses, origin));
        var events = new List<JsonObject>();
        var observed = new Observed(events);
        Outcome outcome;
        try
        {
            var args = Placeholders.Substitute(call["args"], server.Origin) as JsonArray
                ?? throw new ContractCaseException("call.args is not an array");
            outcome = await CallAsync(
                server.Origin, apiKey, options, transport, method, args, cancelAfterMs, stopAfterRetryEvents, observed);
        }
        finally
        {
            await server.DisposeAsync();
        }

        if (outcome.TimedOut)
            return [$"the call did not return within {CaseTimeout}"];

        var problems = new List<string>(server.Problems);
        var origin = server.Origin;
        var substitutedRequests = (JsonArray)Placeholders.Substitute(wantRequests, origin)!;
        AddDiff(problems, "requests", DescribeRequests(server.Requests, substitutedRequests, apiKey), substitutedRequests);
        AddDiff(problems, "OnRequest / OnRetry events", DescribeEvents(observed.Snapshot(), wantEvents), wantEvents);
        if (checkResult)
        {
            var gotResult = outcome.Error is { } error
                ? new JsonObject { ["ok"] = false, ["error"] = NormalizeError(error) }
                : new JsonObject { ["ok"] = true, ["value"] = outcome.Value };
            AddDiff(problems, "result", gotResult, Placeholders.Substitute(expect["result"], origin));
        }

        return problems;
    }

    private static void AddDiff(List<string> problems, string label, JsonNode? got, JsonNode? want)
    {
        var diff = CanonicalJson.Diff(label, got, want);
        if (diff.Length > 0)
            problems.Add(diff);
    }

    // ------------------------------------------------------------------ //
    // Calling the SDK
    // ------------------------------------------------------------------ //

    private sealed record Outcome(JsonNode? Value, Exception? Error, bool TimedOut);

    /// <summary>The hook events, in order, as the contract shapes them.</summary>
    private sealed class Observed(List<JsonObject> events)
    {
        private readonly object _gate = new();
        private int _retries;

        public void Request(RequestEvent e)
        {
            lock (_gate)
                events.Add(new JsonObject { ["type"] = "request", ["attempt"] = e.Attempt });
        }

        /// <summary>Records a retry event; returns the number of retry events so far.</summary>
        public int Retry(RetryEvent e)
        {
            lock (_gate)
            {
                // N-DURATION-MS: the delay in integer milliseconds.
                var delayMs = (long)Math.Round(e.Delay.TotalMilliseconds, MidpointRounding.AwayFromZero);
                events.Add(new JsonObject { ["type"] = "retry", ["attempt"] = e.Attempt, ["delayMs"] = delayMs });
                return ++_retries;
            }
        }

        public List<JsonObject> Snapshot()
        {
            lock (_gate)
                return [.. events.Select(e => (JsonObject)e.DeepClone())];
        }
    }

    private static async Task<Outcome> CallAsync(
        string origin,
        string apiKey,
        JsonObject options,
        ClientTransport transport,
        string method,
        JsonArray args,
        int? cancelAfterMs,
        int? stopAfterRetryEvents,
        Observed observed)
    {
        using var cts = new CancellationTokenSource();
        using var injected = transport == ClientTransport.Injected ? new HttpClient() : null;
        try
        {
            using var client = new PoliPageClient(new PoliPageClientOptions
            {
                ApiKey = apiKey,
                BaseUrl = new Uri(origin),
                MaxRetries = options["maxRetries"]!.GetValue<int>(),
                RetryDelay = TimeSpan.FromMilliseconds(options["retryDelayMs"]!.GetValue<int>()),
                RequestTimeout = TimeSpan.FromMilliseconds(options["timeoutMs"]!.GetValue<int>()),
                HttpClient = injected,
                DownloadHttpClient = injected,
                OnRequest = observed.Request,
                OnRetry = e =>
                {
                    // stopAfterRetryEvents: cancel the call at that retry event.
                    if (observed.Retry(e) == stopAfterRetryEvents)
                        cts.Cancel();
                },
            });
            if (cancelAfterMs is { } ms)
                cts.CancelAfter(ms);

            var value = await InvokeAsync(client, method, args, cts.Token).WaitAsync(CaseTimeout);
            return new Outcome(value, null, false);
        }
        catch (TimeoutException)
        {
            await cts.CancelAsync();
            return new Outcome(null, null, true);
        }
        catch (Exception ex) when (ex is not (ContractCaseException or JsonException))
        {
            // The SDK's error, from the constructor or the call. An error that is not a
            // PoliPageException shows up in the diff.
            return new Outcome(null, ex, false);
        }
    }

    private static async Task<JsonNode?> InvokeAsync(
        PoliPageClient client, string method, JsonArray args, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "render.pdf":
                ArgCount(args, 1, 1);
                return Bytes(await client.Render.PdfAsync(Arg<RenderInput>(args, 0), cancellationToken: cancellationToken));
            case "render.pdfStream":
                {
                    ArgCount(args, 1, 1);
                    var stream = await client.Render.PdfStreamAsync(Arg<RenderInput>(args, 0), cancellationToken: cancellationToken);
                    await using (stream)
                    {
                        using var copy = new MemoryStream();
                        await stream.CopyToAsync(copy, cancellationToken);
                        return Bytes(copy.ToArray());
                    }
                }

            case "render.document":
                ArgCount(args, 1, 1);
                return Descriptor(await client.Render.DocumentAsync(Arg<RenderInput>(args, 0), cancellationToken: cancellationToken));
            case "documents.get":
                ArgCount(args, 1, 1);
                return Descriptor(await client.Documents.GetAsync(Arg<string>(args, 0), cancellationToken: cancellationToken));
            case "documents.preview":
                {
                    ArgCount(args, 1, 1);
                    var preview = await client.Documents.PreviewAsync(Arg<string>(args, 0), cancellationToken: cancellationToken);
                    // N-PREVIEW
                    return new JsonObject { ["html"] = preview.Html, ["pageCount"] = preview.PageCount };
                }

            case "documents.thumbnails":
                {
                    ArgCount(args, 1, 2);
                    var thumbnailOptions = args.Count > 1 ? Arg<ThumbnailOptions>(args, 1) : null;
                    var thumbnails = await client.Documents.ThumbnailsAsync(
                        Arg<string>(args, 0), thumbnailOptions, cancellationToken: cancellationToken);
                    // N-THUMBS-ARRAY: the SDK already returns the unwrapped list.
                    return JsonSerializer.SerializeToNode(thumbnails);
                }

            case "documents.delete":
                ArgCount(args, 1, 1);
                await client.Documents.DeleteAsync(Arg<string>(args, 0), cancellationToken: cancellationToken);
                return null; // N-DELETE-NULL
            default:
                throw new ContractCaseException($"unknown SDK method in the contract: {method}");
        }
    }

    private static void ArgCount(JsonArray args, int min, int max)
    {
        if (args.Count < min || args.Count > max)
            throw new ContractCaseException($"call.args has {args.Count} values, want {min} to {max}");
    }

    private static T Arg<T>(JsonArray args, int index)
    {
        try
        {
            return args[index].Deserialize<T>(ArgsOptions)
                ?? throw new ContractCaseException($"call.args[{index}] is null");
        }
        catch (JsonException ex)
        {
            throw new ContractCaseException($"call.args[{index}]: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ //
    // Normalizations (policy.normalizations)
    // ------------------------------------------------------------------ //

    /// <summary>N-BYTES.</summary>
    private static JsonObject Bytes(byte[] data)
        => new()
        {
            ["sha256"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            ["length"] = data.Length,
        };

    /// <summary>
    /// N-DESCRIPTOR: the fields of sdk-specification.md §7.1, as JSON. The SDK parses the
    /// instants into <see cref="DateTimeOffset"/>; they are written back as the contract
    /// writes them (ISO 8601 UTC with milliseconds), and the orientation as its wire literal.
    /// </summary>
    private static JsonObject Descriptor(DocumentDescriptor d)
        => new()
        {
            ["documentId"] = d.DocumentId,
            ["organizationId"] = d.OrganizationId,
            ["projectId"] = d.ProjectId,
            ["projectSlug"] = d.ProjectSlug,
            ["templateId"] = d.TemplateId,
            ["templateSlug"] = d.TemplateSlug,
            ["environment"] = d.Environment,
            ["apiKeyId"] = d.ApiKeyId,
            ["format"] = d.Format,
            ["orientation"] = JsonSerializer.SerializeToNode(d.Orientation, ArgsOptions),
            ["locale"] = d.Locale,
            ["pageCount"] = d.PageCount,
            ["sizeBytes"] = d.SizeBytes,
            ["createdAt"] = Instant(d.CreatedAt),
            ["metadata"] = JsonSerializer.SerializeToNode(d.Metadata),
            ["presignedPdfUrl"] = d.PresignedPdfUrl,
            ["expiresAt"] = Instant(d.ExpiresAt),
        };

    private static string Instant(DateTimeOffset at)
        => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>N-STATUS-NULL and N-REQID-NULL.</summary>
    private static JsonObject NormalizeError(Exception error)
    {
        if (error is not PoliPageException e)
        {
            return new JsonObject
            {
                ["code"] = "not a PoliPageException",
                ["status"] = null,
                ["message"] = $"{error.GetType().FullName}: {error.Message}",
                ["requestId"] = null,
                ["retryable"] = false,
                ["isAuthError"] = false,
                ["isRateLimitError"] = false,
                ["isValidationError"] = false,
                ["isNetworkError"] = false,
            };
        }

        return new JsonObject
        {
            ["code"] = e.Code,
            ["status"] = e.StatusCode == 0 ? null : e.StatusCode,
            ["message"] = e.Message,
            ["requestId"] = e.RequestId,
            ["retryable"] = e.IsRetryable(),
            ["isAuthError"] = e.IsAuthError(),
            ["isRateLimitError"] = e.IsRateLimitError(),
            ["isValidationError"] = e.IsValidationError(),
            ["isNetworkError"] = e.IsNetworkError(),
        };
    }

    // ------------------------------------------------------------------ //
    // The mock's answers
    // ------------------------------------------------------------------ //

    private static List<MockStep> BuildSteps(JsonArray responses, string origin)
    {
        var steps = new List<MockStep>();
        for (var i = 0; i < responses.Count; i++)
        {
            var step = responses[i] as JsonObject ?? throw new ContractCaseException($"responses[{i}] is not an object");
            steps.Add(BuildStep(step, i, origin));
        }

        return steps;
    }

    private static MockStep BuildStep(JsonObject step, int index, string origin)
    {
        if (step["fault"] is { } fault)
        {
            if (step.Count != 1)
                throw new ContractCaseException($"responses[{index}]: a fault has no status, headers or body");
            return fault.GetValue<string>() switch
            {
                "drop" => MockStep.Drop,
                "hang" => MockStep.Hang,
                var other => throw new ContractCaseException($"responses[{index}]: unknown fault \"{other}\""),
            };
        }

        CheckFields(step, AnswerFields, $"responses[{index}]");
        var status = step["status"]?.GetValue<int>() ?? throw new ContractCaseException($"responses[{index}]: no status");
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in step["headers"] as JsonObject ?? [])
            headers.Add(new(name, Placeholders.Substitute(value!.GetValue<string>(), origin)));

        var bodies = BodyFields.Count(step.ContainsKey);
        if (bodies > 1)
            throw new ContractCaseException($"responses[{index}]: more than one of json, text, base64");

        byte[] body = [];
        if (step.ContainsKey("json"))
        {
            body = Encoding.UTF8.GetBytes(Placeholders.Substitute(step["json"], origin)?.ToJsonString() ?? "null");
            if (!headers.Exists(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)))
                headers.Add(new("Content-Type", "application/json"));
        }
        else if (step["text"] is { } text)
        {
            body = Encoding.UTF8.GetBytes(Placeholders.Substitute(text.GetValue<string>(), origin));
        }
        else if (step["base64"] is { } base64)
        {
            body = Convert.FromBase64String(base64.GetValue<string>());
        }

        return new MockStep(status, headers, body, null);
    }

    // ------------------------------------------------------------------ //
    // Comparing
    // ------------------------------------------------------------------ //

    /// <summary>
    /// The expectations, cut at the <c>stopAfterRetryEvents</c>-th retry event when the case
    /// sets one: then only the events and requests up to there are checked, not the result.
    /// </summary>
    private static (JsonArray Events, JsonArray Requests, bool CheckResult) ExpectedPrefix(
        JsonObject expect, int? stopAfterRetryEvents)
    {
        var events = expect["events"] as JsonArray ?? throw new ContractCaseException("expect.events is missing");
        var requests = expect["requests"] as JsonArray ?? throw new ContractCaseException("expect.requests is missing");
        if (stopAfterRetryEvents is not { } stop)
            return (events, requests, true);

        var retries = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i]?["type"]?.GetValue<string>() != "retry" || ++retries != stop)
                continue;

            var prefix = events.Take(i + 1).ToList();
            var attempts = prefix.Count(e => e?["type"]?.GetValue<string>() == "request");
            if (attempts > requests.Count)
                throw new ContractCaseException($"stopAfterRetryEvents {stop}: more request events than requests");
            return (
                new JsonArray([.. prefix.Select(e => e?.DeepClone())]),
                new JsonArray([.. requests.Take(attempts).Select(r => r?.DeepClone())]),
                false);
        }

        throw new ContractCaseException($"stopAfterRetryEvents {stop}: the case has fewer retries");
    }

    /// <summary>
    /// The recorded requests in the shape of <c>expect.requests</c>. A field that breaks the
    /// expectation keeps the observed value, so the diff shows it.
    /// </summary>
    private static JsonArray DescribeRequests(IReadOnlyList<RecordedRequest> recorded, JsonArray expected, string apiKey)
    {
        var described = new JsonArray();
        string? firstKey = null;
        for (var i = 0; i < recorded.Count; i++)
        {
            var request = recorded[i];
            var want = i < expected.Count ? expected[i] as JsonObject : null;
            var item = new JsonObject
            {
                ["method"] = request.Method,
                ["path"] = request.Target,
                ["auth"] = Auth(request, apiKey),
                ["idempotencyKey"] = IdempotencyKey(request, ref firstKey),
            };
            if (want?.ContainsKey("json") == true)
                item["json"] = Body(request);
            if (want?["minGapMsBefore"] is { } minGap)
                item["minGapMsBefore"] = Gap(recorded, i, minGap.GetValue<double>());
            described.Add(item);
        }

        return described;
    }

    private static string Auth(RecordedRequest request, string apiKey)
    {
        var values = request.Header("Authorization");
        if (values is null)
            return "none";
        return values.Count == 1 && values[0] == "Bearer " + apiKey
            ? "bearer"
            : "unexpected Authorization: " + string.Join(", ", values);
    }

    private static string IdempotencyKey(RecordedRequest request, ref string? firstKey)
    {
        var values = request.Header("Idempotency-Key");
        if (values is null)
            return "absent";
        var key = string.Join(", ", values);
        if (!UuidV4().IsMatch(key))
            return "not a lowercase UUID v4: " + key;
        if (firstKey is null || key == firstKey)
        {
            firstKey = key;
            return "stable";
        }

        return $"changed: {firstKey} then {key}";
    }

    private static JsonNode? Body(RecordedRequest request)
    {
        try
        {
            return JsonNode.Parse(request.Body);
        }
        catch (JsonException)
        {
            return "body is not JSON: " + Encoding.UTF8.GetString(request.Body);
        }
    }

    private static JsonValue Gap(IReadOnlyList<RecordedRequest> recorded, int index, double minGap)
    {
        // N-DURATION-MS: the gap since the previous request, in integer milliseconds.
        var gap = index == 0
            ? 0
            : Math.Round(
                (recorded[index].ArrivedAt - recorded[index - 1].ArrivedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                MidpointRounding.AwayFromZero);
        return gap >= minGap
            ? JsonValue.Create(minGap)
            : JsonValue.Create(string.Create(CultureInfo.InvariantCulture, $"only {gap} ms"));
    }

    /// <summary>The observed events, a delay inside an expected range shown as that range.</summary>
    private static JsonArray DescribeEvents(List<JsonObject> observed, JsonArray expected)
    {
        var described = new JsonArray();
        for (var i = 0; i < observed.Count; i++)
        {
            var item = observed[i];
            if (i < expected.Count
                && item["delayMs"] is { } delay
                && expected[i]?["delayMs"] is JsonObject range
                && delay.GetValue<long>() >= range["min"]!.GetValue<double>()
                && delay.GetValue<long>() < range["max"]!.GetValue<double>())
            {
                item["delayMs"] = range.DeepClone();
            }

            described.Add(item);
        }

        return described;
    }

    // ------------------------------------------------------------------ //
    // Reading the case strictly
    // ------------------------------------------------------------------ //

    private static JsonObject Obj(JsonObject parent, string name)
        => parent[name] as JsonObject ?? throw new ContractCaseException($"{name} is missing");

    /// <summary>A field this harness does not know fails the case instead of being ignored.</summary>
    private static void CheckFields(JsonObject obj, string[] known, string where)
    {
        foreach (var (name, _) in obj)
        {
            if (!known.Contains(name, StringComparer.Ordinal))
                throw new ContractCaseException($"{where}: unknown field \"{name}\"");
        }
    }
}
