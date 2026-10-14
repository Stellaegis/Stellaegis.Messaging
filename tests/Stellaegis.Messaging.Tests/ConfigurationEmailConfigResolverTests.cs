using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;
using static Stellaegis.Messaging.Tests.Spaces;

namespace Stellaegis.Messaging.Tests;

public sealed class ConfigurationEmailConfigResolverTests
{
    private const string Prefix = "Stellaegis:Messaging:Configuration:";

    // The relay as an appsettings.json would describe it, the password left out for another source.
    private static readonly Dictionary<string, string?> Relay = new()
    {
        [Prefix + "Transports:default:Host"] = "smtp.example.test",
        [Prefix + "Transports:default:Port"] = "465",
        [Prefix + "Transports:default:Security"] = "SslOnConnect",
        [Prefix + "Transports:default:Auth"] = "Basic",
        [Prefix + "Transports:default:Credentials:Username"] = "relay-user",
        [Prefix + "Transports:default:FromPolicy:Kind"] = "DomainAuthorized",
        [Prefix + "Transports:default:FromPolicy:Domains:0"] = "example.test",
        [Prefix + "Transports:default:DefaultFrom:Address"] = "noreply@example.test",
        [Prefix + "Transports:default:DefaultFrom:Name"] = "Example",
        [Prefix + "FromAddresses:0:Address"] = "billing@example.test",
        [Prefix + "FromAddresses:0:ReplyTo"] = "accounts@example.test",
    };

    // A second source on top of the first, as environment variables are.
    private static readonly Dictionary<string, string?> Password = new()
    {
        [Prefix + "Transports:default:Credentials:Password"] = "from-the-environment",
    };

    private static IEmailConfigResolver Build(bool inheritSharedSpace = false, params Dictionary<string, string?>[] sources)
    {
        var builder = new ConfigurationBuilder();
        foreach (var source in sources)
            builder.AddInMemoryCollection(source);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stellaegis:Messaging:InheritSharedSpace"] = inheritSharedSpace.ToString(),
        });
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddStellaegisEmail(configuration);
        services.AddStellaegisEmailConfiguration(configuration);
        return services.BuildServiceProvider().GetRequiredService<IEmailConfigResolver>();
    }

    [Fact]
    public async Task Section_binds_into_a_whole_configuration_with_a_field_from_another_source()
    {
        var config = await Build(false, Relay, Password).ResolveAsync(Space.Shared);

        Assert.NotNull(config);
        var transport = config.Transports["default"];
        Assert.Equal("smtp.example.test", transport.Host);
        Assert.Equal(465, transport.Port);
        Assert.Equal(SmtpSecurity.SslOnConnect, transport.Security);
        Assert.Equal(TransportAuth.Basic, transport.Auth);
        Assert.Equal("relay-user", transport.Credentials!.Username);
        Assert.Equal("from-the-environment", transport.Credentials.Password);
        Assert.Equal(FromPolicyKind.DomainAuthorized, transport.FromPolicy!.Kind);
        Assert.Equal(["example.test"], transport.FromPolicy.Domains);
        Assert.Equal(new EmailAddress("noreply@example.test", "Example"), transport.DefaultFrom);
        Assert.Equal("accounts@example.test", Assert.Single(config.FromAddresses).ReplyTo);
    }

    [Fact]
    public async Task Compartment_does_not_use_the_section_by_default()
    {
        Assert.Null(await Build(false, Relay).ResolveAsync(Acme));
    }

    [Fact]
    public async Task Compartment_uses_the_section_when_it_inherits_the_shared_space()
    {
        var config = await Build(true, Relay).ResolveAsync(Acme);

        Assert.Equal("smtp.example.test", config!.Transports["default"].Host);
    }

    [Fact]
    public async Task Absent_section_means_no_relay_configured()
    {
        Assert.Null(await Build(false).ResolveAsync(Space.Shared));
    }

    [Fact]
    public async Task Engine_sends_through_the_section_without_any_vault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Relay).Build();
        var transport = new FakeEmailTransport();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddStellaegisEmail(configuration);
        services.AddStellaegisEmailConfiguration(configuration);
        services.AddSingleton<IEmailTransport>(transport);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IEmailSender>().SendAsync(Space.Shared, new EmailMessage
        {
            To = [new EmailAddress("dest@example.test")],
            Subject = "Hello",
            HtmlBody = "<p>Hi</p>",
        });

        var outbox = provider.GetRequiredService<Outbox.InMemoryMessageOutboxStore>();
        var entry = Assert.Single(await outbox.ClaimDueAsync(10, TimeSpan.FromMinutes(1)));
        Assert.Equal(Space.Shared, entry.Space);
        Assert.Equal("noreply@example.test", entry.Message.From!.Address);
    }
}
