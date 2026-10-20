using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stellaegis.Messaging.Tests;

public sealed class MessagingStartupCheckTests
{
    private static async Task StartAsync(bool configurationSource, bool transport)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddStellaegisEmail(configuration);
        if (configurationSource)
            services.AddStellaegisEmailConfiguration(configuration);
        if (transport)
            services.AddSingleton<IEmailTransport>(new FakeEmailTransport());

        await using var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().OfType<MessagingStartupCheck>().Single();
        await check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Complete_assembly_starts()
    {
        await StartAsync(configurationSource: true, transport: true);
    }

    [Fact]
    public async Task Assembly_without_a_configuration_source_names_both_sources()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(configurationSource: false, transport: true));

        Assert.Contains("AddStellaegisEmailConfiguration", exception.Message);
        Assert.Contains("AddStellaegisEmailVaultConfiguration", exception.Message);
        Assert.DoesNotContain("AddStellaegisMailKitTransport", exception.Message);
    }

    [Fact]
    public async Task Assembly_without_a_transport_names_the_transport()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(configurationSource: true, transport: false));

        Assert.Contains("AddStellaegisMailKitTransport", exception.Message);
        Assert.DoesNotContain("AddStellaegisEmailConfiguration", exception.Message);
    }

    [Fact]
    public void Check_starts_before_the_outbox_worker()
    {
        var services = new ServiceCollection();
        services.AddStellaegisEmail(new ConfigurationBuilder().Build());

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();

        Assert.True(hosted.IndexOf(typeof(MessagingStartupCheck)) < hosted.IndexOf(typeof(Outbox.OutboxProcessor)));
    }
}
