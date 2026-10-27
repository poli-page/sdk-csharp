using FluentAssertions;
using Xunit;

namespace PoliPage.IntegrationTests;

// No Integration trait: these check the skip guard itself, so ci.yml runs them.
public sealed class SandboxGuardTests
{
    private const string Hex = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("pp_live_" + Hex)]
    [InlineData("pp_staging_" + Hex)]
    [InlineData("pp_sandbox-eu_" + Hex)]
    [InlineData("pp_sandboxed_" + Hex)]
    [InlineData("PP_SANDBOX_" + Hex)]
    public void SkipReason_skips_without_a_sandbox_key(string? apiKey)
    {
        Sandbox.SkipReason(apiKey).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SkipReason_runs_with_a_sandbox_key()
    {
        Sandbox.SkipReason("pp_sandbox_" + Hex).Should().BeNull();
    }
}
