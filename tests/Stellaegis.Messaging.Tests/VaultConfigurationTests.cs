using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Secrets;
using Stellaegis.Primitives;
using Stellaegis.Secrets;
using static Stellaegis.Messaging.Tests.Spaces;

namespace Stellaegis.Messaging.Tests;

public sealed class VaultConfigurationTests
{
    private static readonly SecretPath SharedPath = SecretPath.Parse("messaging._shared");
    private static readonly SecretPath AcmePath = SecretPath.Parse("messaging.acme");

    private static string Blob(string host, string? password = null) => JsonSerializer.Serialize(new
    {
        transports = new { @default = new { host, credentials = new { username = "user", password } } },
    });

    private static string? HostOf(MessagingConfig? config) => config?.Transports["default"].Host;

    private static (ServiceProvider Provider, DictionarySecretsProvider Vault) Build(bool inheritSharedSpace = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stellaegis:Messaging:InheritSharedSpace"] = inheritSharedSpace.ToString(),
            })
            .Build();

        var vault = new DictionarySecretsProvider();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISecretsProvider>(vault);
        services.AddSingleton<IMutableSecretsProvider>(vault);
        services.AddStellaegisEmailVaultConfiguration(configuration);
        services.AddStellaegisMessagingConfigStore(configuration);
        return (services.BuildServiceProvider(), vault);
    }

    private static Task<MessagingConfig?> ResolveAsync(ServiceProvider provider, Space space)
        => provider.CreateScope().ServiceProvider.GetRequiredService<IEmailConfigResolver>().ResolveAsync(space);

    private static IMessagingConfigStore Store(ServiceProvider provider)
        => provider.CreateScope().ServiceProvider.GetRequiredService<IMessagingConfigStore>();

    [Fact]
    public async Task Compartment_reads_its_own_path()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("acme.example.test");

        Assert.Equal("acme.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Shared_space_reads_the_shared_path()
    {
        var (provider, vault) = Build();
        vault.Values[SharedPath] = Blob("shared.example.test");

        Assert.Equal("shared.example.test", HostOf(await ResolveAsync(provider, Space.Shared)));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("default")]
    public async Task No_compartment_name_reaches_the_shared_configuration(string name)
    {
        // Written through the store, so that the module's own path rule decides where it lands.
        var (provider, _) = Build();
        var shared = new MessagingConfig
        {
            Transports = new Dictionary<string, TransportConfig> { ["default"] = new() { Host = "shared.example.test" } },
        };
        await Store(provider).SaveAsync(Space.Shared, shared);

        Assert.Null(await ResolveAsync(provider, Compartment.Parse(name)));
        Assert.Null(await Store(provider).GetAsync(Compartment.Parse(name)));
    }

    [Fact]
    public async Task Compartment_without_configuration_does_not_inherit_by_default()
    {
        var (provider, vault) = Build();
        vault.Values[SharedPath] = Blob("shared.example.test");

        Assert.Null(await ResolveAsync(provider, Acme));
    }

    [Fact]
    public async Task Compartment_without_configuration_inherits_when_the_option_is_set()
    {
        var (provider, vault) = Build(inheritSharedSpace: true);
        vault.Values[SharedPath] = Blob("shared.example.test");

        Assert.Equal("shared.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Invalidating_a_path_evicts_what_was_read_from_it()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("old.example.test");
        await ResolveAsync(provider, Acme);

        vault.Values[AcmePath] = Blob("new.example.test");
        Assert.Equal("old.example.test", HostOf(await ResolveAsync(provider, Acme)));

        provider.GetRequiredService<IMessagingConfigCache>().Invalidate(AcmePath);
        Assert.Equal("new.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Adding_a_compartment_configuration_evicts_an_inherited_one()
    {
        var (provider, vault) = Build(inheritSharedSpace: true);
        vault.Values[SharedPath] = Blob("shared.example.test");
        await ResolveAsync(provider, Acme);

        vault.Values[AcmePath] = Blob("acme.example.test");
        provider.GetRequiredService<IMessagingConfigCache>().Invalidate(AcmePath);

        Assert.Equal("acme.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Invalidating_another_path_evicts_nothing()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("old.example.test");
        await ResolveAsync(provider, Acme);

        vault.Values[AcmePath] = Blob("new.example.test");
        provider.GetRequiredService<IMessagingConfigCache>().Invalidate(SecretPath.Parse("tenantDB.primary.postgresql.acme"));

        Assert.Equal("old.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Saving_through_the_store_evicts_in_this_process()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("old.example.test");
        await ResolveAsync(provider, Acme);

        var edited = new MessagingConfig
        {
            Transports = new Dictionary<string, TransportConfig> { ["default"] = new() { Host = "new.example.test" } },
        };
        await Store(provider).SaveAsync(Acme, edited);

        Assert.Equal("new.example.test", HostOf(await ResolveAsync(provider, Acme)));
    }

    [Fact]
    public async Task Deleting_through_the_store_evicts_in_this_process()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("acme.example.test");
        await ResolveAsync(provider, Acme);

        await Store(provider).DeleteAsync(Acme);

        Assert.Null(await ResolveAsync(provider, Acme));
    }

    [Fact]
    public async Task Store_masks_secrets_on_read_and_keeps_them_on_a_masked_save()
    {
        var (provider, vault) = Build();
        vault.Values[AcmePath] = Blob("acme.example.test", password: "s3cret");
        var store = Store(provider);

        var read = await store.GetAsync(Acme);
        Assert.Equal(MessagingConfigMasking.MaskedSecret, read!.Transports["default"].Credentials!.Password);

        await store.SaveAsync(Acme, read);

        var stored = await ResolveAsync(provider, Acme);
        Assert.Equal("s3cret", stored!.Transports["default"].Credentials!.Password);
    }

    [Fact]
    public async Task Store_never_falls_back_to_the_shared_space()
    {
        var (provider, vault) = Build(inheritSharedSpace: true);
        vault.Values[SharedPath] = Blob("shared.example.test");

        Assert.Null(await Store(provider).GetAsync(Acme));
    }

    private sealed class DictionarySecretsProvider : IMutableSecretsProvider
    {
        public Dictionary<SecretPath, string> Values { get; } = [];

        public Task<string> GetSecretAsync(SecretPath path, CancellationToken cancellationToken = default)
            => Values.TryGetValue(path, out var value)
                ? Task.FromResult(value)
                : throw new KeyNotFoundException($"Secret not found: {path}");

        public Task<IReadOnlyDictionary<SecretPath, string>> GetSecretsAsync(
            IEnumerable<SecretPath> paths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<SecretPath, string>>(
                paths.Where(Values.ContainsKey).ToDictionary(p => p, p => Values[p]));

        public Task SetSecretAsync(SecretPath path, string value, CancellationToken ct = default)
        {
            Values[path] = value;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(SecretPath path, CancellationToken ct = default)
        {
            Values.Remove(path);
            return Task.CompletedTask;
        }
    }
}
