using FluentAssertions;
using Xunit;

namespace PoliPage.IntegrationTests;

public sealed class SandboxRenderTests
{
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46];

    [SandboxFact]
    [Trait("Category", "Integration")]
    public async Task PdfAsync_renders_getting_started_welcome()
    {
        using var client = Sandbox.CreateClient();

        var pdf = await client.Render.PdfAsync(Sandbox.Welcome);

        pdf.Should().StartWith(PdfMagicBytes);
    }

    [SandboxFact]
    [Trait("Category", "Integration")]
    public async Task DocumentAsync_then_get_preview_and_delete_the_stored_document()
    {
        using var client = Sandbox.CreateClient();

        var doc = await client.Render.DocumentAsync(Sandbox.Welcome);
        doc.DocumentId.Should().NotBeNullOrEmpty();
        doc.Environment.Should().Be("sandbox");
        doc.PageCount.Should().BeGreaterThan(0);

        var fetched = await client.Documents.GetAsync(doc.DocumentId);
        fetched.DocumentId.Should().Be(doc.DocumentId);

        var preview = await client.Documents.PreviewAsync(doc.DocumentId);
        preview.Html.Should().NotBeNullOrWhiteSpace();
        preview.PageCount.Should().Be(doc.PageCount);

        await client.Documents.DeleteAsync(doc.DocumentId);
    }

    [SandboxFact]
    [Trait("Category", "Integration")]
    public async Task PdfAsync_of_a_missing_template_throws_TEMPLATE_NOT_FOUND()
    {
        using var client = Sandbox.CreateClient();

        var act = () => client.Render.PdfAsync(new RenderInput
        {
            Project = "getting-started",
            Template = "does-not-exist",
            Data = new { },
        });

        var thrown = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.TemplateNotFound);
        thrown.Which.StatusCode.Should().Be(404);
    }
}
