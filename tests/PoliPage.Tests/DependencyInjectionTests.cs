using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace PoliPage.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddPoliPage_registers_PoliPageClient_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddPoliPage(o => o.ApiKey = "pp_test_unit");

        using var sp = services.BuildServiceProvider();
        var c1 = sp.GetRequiredService<PoliPageClient>();
        var c2 = sp.GetRequiredService<PoliPageClient>();

        c1.Should().BeSameAs(c2, "AddPoliPage must register a singleton");
    }

    [Fact]
    public void AddPoliPage_resolves_options_with_configured_ApiKey()
    {
        var services = new ServiceCollection();
        services.AddPoliPage(o =>
        {
            o.ApiKey = "pp_test_resolved";
            o.MaxRetries = 5;
        });

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;

        opts.ApiKey.Should().Be("pp_test_resolved");
        opts.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void AddPoliPage_wires_IHttpClientFactory_named_clients()
    {
        var services = new ServiceCollection();
        services.AddPoliPage(o => o.ApiKey = "pp_test_unit");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        var api = factory.CreateClient("PoliPage");
        var download = factory.CreateClient("PoliPage.Download");

        api.Should().NotBeNull();
        download.Should().NotBeNull();
        api.Should().NotBeSameAs(download);
    }

    [Fact]
    public void AddPoliPage_throws_at_startup_when_ApiKey_is_missing()
    {
        // Verify ValidateOnStart catches a missing ApiKey before the first SDK call.
        var services = new ServiceCollection();
        services.AddPoliPage(_ => { /* deliberately leave ApiKey unset */ });

        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<PoliPageClient>();

        // OptionsValidationException is the BCL type emitted by ValidateOnStart.
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ApiKey is required*");
    }

    [Fact]
    public void AddPoliPage_throws_at_startup_when_MaxRetries_is_negative()
    {
        var services = new ServiceCollection();
        services.AddPoliPage(o =>
        {
            o.ApiKey = "pp_test_unit";
            o.MaxRetries = -1;
        });

        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<PoliPageClient>();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MaxRetries*");
    }

    [Fact]
    public void AddPoliPage_throws_ArgumentNullException_when_services_is_null()
    {
        IServiceCollection services = null!;
        var act = () => services.AddPoliPage(o => o.ApiKey = "x");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPoliPage_throws_ArgumentNullException_when_configure_is_null()
    {
        var services = new ServiceCollection();
        var act = () => services.AddPoliPage(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }
}
