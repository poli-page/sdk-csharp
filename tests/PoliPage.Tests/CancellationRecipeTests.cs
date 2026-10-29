using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// The catch-and-rethrow recipe of the Cancellation docs page compiles and gives back the
/// standard .NET behaviour: the task ends Canceled, with the caller's token.
/// </summary>
public sealed class CancellationRecipeTests
{
    [Fact]
    public async Task Rethrowing_aborted_as_OperationCanceledException_ends_the_task_Canceled()
    {
        await using var server = new MockServer(_ => [MockStep.Hang]);
        using var client = new PoliPageClient(Options(server));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var task = RenderAsync(client, Input, cts.Token);
        var act = () => task;

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        task.IsCanceled.Should().BeTrue();
        thrown.Which.CancellationToken.Should().Be(cts.Token);
        thrown.Which.InnerException.Should().BeOfType<PoliPageException>()
            .Which.Code.Should().Be(PoliPageErrorCode.Aborted);
    }

    [Fact]
    public async Task Without_the_recipe_a_cancelled_call_ends_Faulted()
    {
        await using var server = new MockServer(_ => [MockStep.Hang]);
        using var client = new PoliPageClient(Options(server));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var task = client.Render.PdfAsync(Input, cancellationToken: cts.Token);
        var act = () => task;

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.Aborted);
        task.IsFaulted.Should().BeTrue();
    }

    // The recipe, as the docs show it.
    private static async Task<byte[]> RenderAsync(PoliPageClient client, RenderInput input, CancellationToken cancellationToken)
    {
        try
        {
            return await client.Render.PdfAsync(input, cancellationToken: cancellationToken);
        }
        catch (PoliPageException ex) when (ex.Code == PoliPageErrorCode.Aborted)
        {
            // The task now ends Canceled, and ASP.NET Core sees a client abort.
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }
}
