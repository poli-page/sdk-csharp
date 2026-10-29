using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// An empty document id is URL-encoded into the path like any other and the API answers it
/// (spec §7; contract case <c>documents.get.not-found-route</c> covers GetAsync).
/// </summary>
public sealed class EmptyDocumentIdTests
{
    private static MockStep NotFound()
        => JsonAnswer(404, """{"error":"NOT_FOUND","requestId":"req_nf"}""");

    [Fact]
    public async Task PreviewAsync_sends_an_empty_id_to_the_API()
    {
        await using var server = new MockServer(_ => [NotFound()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.PreviewAsync("");

        (await act.Should().ThrowAsync<PoliPageNotFoundException>()).Which.Code.Should().Be(PoliPageErrorCode.NotFound);
        server.Requests.Should().ContainSingle().Which.Target.Should().Be("/v1/documents//preview");
    }

    [Fact]
    public async Task ThumbnailsAsync_sends_an_empty_id_to_the_API()
    {
        await using var server = new MockServer(_ => [NotFound()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.ThumbnailsAsync("");

        (await act.Should().ThrowAsync<PoliPageNotFoundException>()).Which.Code.Should().Be(PoliPageErrorCode.NotFound);
        server.Requests.Should().ContainSingle().Which.Target.Should().Be("/v1/documents//thumbnails");
    }

    [Fact]
    public async Task DeleteAsync_sends_a_whitespace_id_encoded()
    {
        await using var server = new MockServer(_ => [NotFound()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.DeleteAsync("  ");

        (await act.Should().ThrowAsync<PoliPageNotFoundException>()).Which.Code.Should().Be(PoliPageErrorCode.NotFound);
        server.Requests.Should().ContainSingle().Which.Target.Should().Be("/v1/documents/%20%20");
    }
}
