using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class RenderInputTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter<PageFormat>(),
            new System.Text.Json.Serialization.JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
        },
    };

    [Fact]
    public void ProjectModeInput_serialises_Format_as_PascalCase()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { },
            Format = PageFormat.Letter,
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, Options);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("format").GetString().Should().Be("Letter");
    }

    [Fact]
    public void ProjectModeInput_serialises_Orientation_as_lowercase()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { },
            Orientation = Orientation.Landscape,
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, Options);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("orientation").GetString().Should().Be("landscape");
    }

    [Fact]
    public void ProjectModeInput_serialises_Locale()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { },
            Locale = "fr-FR",
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, Options);
#pragma warning restore IL2026, IL3050

        JsonDocument.Parse(json).RootElement.GetProperty("locale").GetString().Should().Be("fr-FR");
    }

    [Fact]
    public void ProjectModeInput_does_not_serialise_IdempotencyKey_to_wire_body()
    {
        // IdempotencyKey is a header, never a body field. Even when set on the input,
        // serialisation must skip it. (We mark it [JsonIgnore].)
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { },
            IdempotencyKey = "key_abc",
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, Options);
#pragma warning restore IL2026, IL3050

        JsonDocument.Parse(json).RootElement.TryGetProperty("idempotencyKey", out _).Should().BeFalse();
    }

    [Fact]
    public void Omitted_fields_are_absent_from_wire()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { },
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, Options);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("format", out _).Should().BeFalse();
        root.TryGetProperty("orientation", out _).Should().BeFalse();
        root.TryGetProperty("locale", out _).Should().BeFalse();
    }
}
