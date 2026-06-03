using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class DocumentDescriptorTests
{
    private const string SampleJson = """
        {
            "documentId": "doc_abc123",
            "organizationId": "org_xyz",
            "projectId": "prj_b",
            "projectSlug": "billing",
            "templateId": "tpl_i",
            "templateSlug": "invoice",
            "version": "1.0.0",
            "environment": "live",
            "apiKeyId": "key_42",
            "format": "pdf",
            "orientation": "landscape",
            "locale": "fr-FR",
            "pageCount": 3,
            "sizeBytes": 12345,
            "createdAt": "2026-06-03T10:00:00Z",
            "presignedPdfUrl": "https://placeholder.invalid/doc_abc123.pdf",
            "expiresAt": "2026-06-03T10:15:00Z"
        }
        """;

#pragma warning disable CA1869
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
        },
    };
#pragma warning restore CA1869

    [Fact]
    public void Deserialise_populates_apiKeyId()
    {
#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(SampleJson, Options);
#pragma warning restore IL2026, IL3050

        d!.ApiKeyId.Should().Be("key_42");
    }

    [Fact]
    public void Deserialise_populates_orientation_as_enum()
    {
#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(SampleJson, Options);
#pragma warning restore IL2026, IL3050

        d!.Orientation.Should().Be(PoliPage.Orientation.Landscape);
    }

    [Fact]
    public void Deserialise_populates_locale()
    {
#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(SampleJson, Options);
#pragma warning restore IL2026, IL3050

        d!.Locale.Should().Be("fr-FR");
    }

    [Fact]
    public void Deserialise_populates_createdAt()
    {
#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(SampleJson, Options);
#pragma warning restore IL2026, IL3050

        d!.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-06-03T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Deserialise_populates_expiresAt()
    {
#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(SampleJson, Options);
#pragma warning restore IL2026, IL3050

        d!.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-06-03T10:15:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Deserialise_tolerates_null_optionals()
    {
        const string MinimalJson = """
            {
                "documentId": "doc_min",
                "organizationId": "org_min",
                "environment": "live",
                "format": "pdf",
                "pageCount": 1,
                "sizeBytes": 1,
                "createdAt": "2026-06-03T10:00:00Z",
                "presignedPdfUrl": "https://x.invalid/d.pdf",
                "expiresAt": "2026-06-03T10:15:00Z"
            }
            """;

#pragma warning disable IL2026, IL3050
        var d = JsonSerializer.Deserialize<DocumentDescriptor>(MinimalJson, Options);
#pragma warning restore IL2026, IL3050

        d!.ApiKeyId.Should().BeNull();
        d.Orientation.Should().BeNull();
        d.Locale.Should().BeNull();
    }
}
