using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class OrientationTests
{
    [Theory]
    [InlineData(Orientation.Portrait, "portrait")]
    [InlineData(Orientation.Landscape, "landscape")]
    public void Orientation_serialises_as_lowercase_wire_literal(Orientation value, string expected)
    {
#pragma warning disable CA1869
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase) },
        };
#pragma warning restore CA1869

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(value, options);
#pragma warning restore IL2026, IL3050

        json.Should().Be($"\"{expected}\"");
    }

    [Fact]
    public void Orientation_has_exactly_2_canonical_values()
    {
        Enum.GetNames<Orientation>().Should().BeEquivalentTo(["Portrait", "Landscape"]);
    }
}
