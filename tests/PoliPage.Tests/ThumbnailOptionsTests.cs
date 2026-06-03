using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class ThumbnailOptionsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<ThumbnailFormat>(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void Quality_serialises_when_set()
    {
        var opt = new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Jpeg, Quality = 85 };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(opt, Options);
#pragma warning restore IL2026, IL3050

        JsonDocument.Parse(json).RootElement.GetProperty("quality").GetInt32().Should().Be(85);
    }

    [Fact]
    public void Pages_serialises_as_int_array_when_set()
    {
        var opt = new ThumbnailOptions { Width = 320, Pages = new[] { 1, 3, 5 } };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(opt, Options);
#pragma warning restore IL2026, IL3050

        var pages = JsonDocument.Parse(json).RootElement.GetProperty("pages");
        pages.GetArrayLength().Should().Be(3);
        pages[0].GetInt32().Should().Be(1);
        pages[1].GetInt32().Should().Be(3);
        pages[2].GetInt32().Should().Be(5);
    }

    [Fact]
    public void Omitted_quality_and_pages_are_absent_from_wire()
    {
        var opt = new ThumbnailOptions { Width = 200 };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(opt, Options);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("quality", out _).Should().BeFalse();
        root.TryGetProperty("pages", out _).Should().BeFalse();
    }

    [Fact]
    public void Quality_serialises_alongside_Format_Jpeg()
    {
        var opt = new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Jpeg, Quality = 90 };

#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(opt, Options);
#pragma warning restore IL2026, IL3050
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("format").GetString().Should().Be("jpeg");
        root.GetProperty("quality").GetInt32().Should().Be(90);
    }
}
