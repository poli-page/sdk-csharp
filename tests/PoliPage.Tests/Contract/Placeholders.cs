using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PoliPage.Tests.Contract;

/// <summary>
/// The contract's placeholders: <c>{{mock}}</c> (the mock server's origin) and
/// <c>{{httpDate+N}}</c> (an HTTP-date N seconds after the replacement). Any other
/// placeholder is an error.
/// </summary>
internal static partial class Placeholders
{
    [GeneratedRegex(@"\{\{([^}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^httpDate\+(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex HttpDate();

    public static string Substitute(string value, string origin)
        => Pattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            if (name == "mock")
                return origin;
            var date = HttpDate().Match(name);
            if (date.Success)
            {
                // An HTTP-date has whole seconds: round up, so the date is never less than N
                // seconds ahead.
                var at = DateTimeOffset.UtcNow.AddSeconds(int.Parse(date.Groups[1].Value, CultureInfo.InvariantCulture));
                var rounded = new DateTimeOffset(
                    (at.UtcTicks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
                    TimeSpan.Zero);
                return rounded.ToString("r", CultureInfo.InvariantCulture);
            }

            throw new InvalidDataException($"unknown contract placeholder {match.Value}");
        });

    /// <summary>A copy of <paramref name="node"/> with every string's placeholders replaced.</summary>
    public static JsonNode? Substitute(JsonNode? node, string origin) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Substitute(p.Value, origin)))),
        JsonArray array => new JsonArray([.. array.Select(item => Substitute(item, origin))]),
        JsonValue value when value.TryGetValue<string>(out var s) => JsonValue.Create(Substitute(s, origin)),
        _ => node.DeepClone(),
    };
}
