using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class PageFormatTests
{
    [Theory]
    [InlineData(PageFormat.A3, "A3")]
    [InlineData(PageFormat.A4, "A4")]
    [InlineData(PageFormat.A5, "A5")]
    [InlineData(PageFormat.A6, "A6")]
    [InlineData(PageFormat.B4, "B4")]
    [InlineData(PageFormat.B5, "B5")]
    [InlineData(PageFormat.Letter, "Letter")]
    [InlineData(PageFormat.Legal, "Legal")]
    [InlineData(PageFormat.Tabloid, "Tabloid")]
    [InlineData(PageFormat.Executive, "Executive")]
    [InlineData(PageFormat.Statement, "Statement")]
    [InlineData(PageFormat.Folio, "Folio")]
    public void PageFormat_serialises_as_PascalCase_wire_literal(PageFormat value, string expected)
    {
#pragma warning disable CA1869
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<PageFormat>() },
        };
#pragma warning restore CA1869

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(value, options);
#pragma warning restore IL2026, IL3050

        json.Should().Be($"\"{expected}\"");
    }

    [Fact]
    public void PageFormat_has_exactly_12_canonical_values()
    {
        Enum.GetNames<PageFormat>().Should().HaveCount(12,
            "the canonical contract (sdk-node/src/types.ts:7-19) is exactly 12 values");
    }
}
