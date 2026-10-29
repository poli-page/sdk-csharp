// The harness converts between JSON and the SDK's types with reflection-based
// System.Text.Json, as the other tests do; trimming and AOT do not apply to test code.
#pragma warning disable IL2026, IL3050
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoliPage.Tests.Contract;

/// <summary>
/// JSON compared by value: object keys sorted, numbers compared by value (<c>2</c> equals
/// <c>2.0</c>). The indented form doubles as the diff shown when a case fails.
/// </summary>
internal static class CanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(JsonNode? node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            WriteNode(writer, node);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>An empty string when equal; otherwise the two values, for the failure message.</summary>
    public static string Diff(string label, JsonNode? got, JsonNode? want)
    {
        var gotText = Write(got);
        var wantText = Write(want);
        return string.Equals(gotText, wantText, StringComparison.Ordinal)
            ? ""
            : $"{label} differ\n  want: {Indent(wantText)}\n  got:  {Indent(gotText)}";
    }

    private static string Indent(string text) => text.Replace("\n", "\n  ", StringComparison.Ordinal);

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                    WriteNode(writer, item);
                writer.WriteEndArray();
                break;
            default:
                WriteValue(writer, node.AsValue());
                break;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonValue value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                writer.WriteRawValue(
                    decimal.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)
                        .ToString("0.############################", CultureInfo.InvariantCulture));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
