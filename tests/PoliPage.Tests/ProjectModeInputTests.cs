using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class ProjectModeInputTests
{
#pragma warning disable CA1869
    private static readonly JsonSerializerOptions OptionsIgnoreNull = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions OptionsCamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
#pragma warning restore CA1869

    [Fact]
    public void Construction_without_Version_compiles_and_serialises_without_version_key()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Data = new { customer = "Acme" },
            // No Version — must compile because Version is optional in the canonical contract.
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, OptionsIgnoreNull);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("project").GetString().Should().Be("billing");
        root.TryGetProperty("version", out _).Should().BeFalse(
            "omitted Version must not be sent on the wire — the API renders the draft");
    }

    [Fact]
    public void Construction_with_Version_serialises_the_version_key()
    {
        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "1.0.0",
            Data = new { customer = "Acme" },
        };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(input, OptionsCamelCase);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("version").GetString().Should().Be("1.0.0");
    }
}
