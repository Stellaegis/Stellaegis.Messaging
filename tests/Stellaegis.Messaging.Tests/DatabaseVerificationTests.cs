using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Messaging.PostgreSQL;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.Tests;

/// <summary>The outbox installed by the fixture, verified as an application does at startup, under its own role.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class DatabaseVerificationTests(PostgreSqlFixture database)
{
    private static IConfiguration Configuration(string connectionString, string? key = "one") => new ConfigurationBuilder().AddInMemoryCollection(
    [
        KeyValuePair.Create<string, string?>("Stellaegis:Messaging:Database:ConnectionString", connectionString),
        KeyValuePair.Create<string, string?>("Stellaegis:Messaging:Database:Keys:1", key),
    ]).Build();

    [Fact]
    public async Task The_installed_outbox_passes_the_verification_of_the_application()
    {
        Assert.SkipWhen(database.Unavailable is not null, database.Unavailable ?? "");
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddStellaegisFrame(ApplicationFrame.ManyCompartments())
            .AddStellaegisEmailPostgreSql(Configuration(database.ConnectionString))
            .AddStellaegisEmailConfiguration(new ConfigurationBuilder().Build())
            .AddSingleton<IEmailTransport>(new FakeEmailTransport())
            .BuildServiceProvider();

        provider.ValidateStellaegisModules();
        await provider.VerifyStellaegisDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([MessagingDatabase.Value], provider.GetStellaegisModuleDatabases());
        Assert.IsType<PostgreSqlMessageOutboxStore>(provider.GetRequiredService<IMessageOutboxStore>());
        Assert.Same(provider.GetRequiredService<IMessageOutboxStore>(), provider.GetRequiredService<IMessageOutbox>());
        Assert.Same(provider.GetRequiredService<IMessageOutboxStore>(), provider.GetRequiredService<IMessageOutboxPurge>());
    }

    [Fact]
    public void The_database_options_are_validated_when_the_host_starts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddStellaegisFrame(ApplicationFrame.ManyCompartments())
            .AddStellaegisEmailPostgreSql(Configuration("Host=nowhere", key: null))
            .AddStellaegisEmailConfiguration(new ConfigurationBuilder().Build())
            .AddSingleton<IEmailTransport>(new FakeEmailTransport());
        using var host = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<MessagingDatabaseOptions>>().Value);

        Assert.Contains("Keys:1", exception.Message);
    }

    [Fact]
    public void The_engine_is_registered_once_whichever_outbox_is_durable()
    {
        using var provider = new ServiceCollection()
            .AddStellaegisFrame(ApplicationFrame.ManyCompartments())
            .AddStellaegisEmail(new ConfigurationBuilder().Build())
            .AddStellaegisEmailPostgreSql(Configuration("Host=nowhere"))
            .BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(provider.ValidateStellaegisModules);

        Assert.Contains("declared 2 times", exception.Message);
    }
}
