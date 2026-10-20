using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stellaegis.Caching;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Secrets;

namespace Stellaegis.Messaging;

/// <summary>
/// Dependency injection helpers for the messaging configuration kept in a vault.
/// </summary>
public static class SecretsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vault-backed <see cref="IEmailConfigResolver"/> — one secret per space,
    /// <c>{compartment}.messaging.configuration</c> or <c>_shared.messaging.configuration</c> — and its bounded
    /// cache, with <see cref="IMessagingConfigCache"/> to evict it. Requires an <c>ISecretsProvider</c>.
    /// Reads options from the <c>Stellaegis:Messaging</c> section.
    /// </summary>
    /// <remarks>
    /// Each cached configuration is attached to the vault paths it depends on. Edits made through
    /// <see cref="IMessagingConfigStore"/> evict in this process; a host that learns of a change made
    /// elsewhere calls <see cref="IMessagingConfigCache.Invalidate"/>. Without that call, the cache TTL is
    /// the only freshness mechanism for edits made in another process.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailVaultConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = VaultConfigurationOptions.SectionName)
    {
        services.AddMessagingConfigCache(configuration, sectionName);
        services.Configure<MessagingOptions>(configuration.GetSection(sectionName));
        services.TryAddScoped<IEmailConfigResolver, VaultEmailConfigResolver>();
        return services;
    }

    /// <summary>
    /// Registers the vault-backed <see cref="IMessagingConfigStore"/> (masked read / merged write)
    /// used by editing surfaces. Requires an <c>IMutableSecretsProvider</c>. Reads the cache options from
    /// the <c>Stellaegis:Messaging</c> section, since the store evicts what it changes.
    /// </summary>
    public static IServiceCollection AddStellaegisMessagingConfigStore(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = VaultConfigurationOptions.SectionName)
    {
        services.AddMessagingConfigCache(configuration, sectionName);
        services.TryAddScoped<IMessagingConfigStore, VaultMessagingConfigStore>();
        return services;
    }

    // Shared by the resolver and the store, which evicts what it changes. Safe to call twice: the bounded
    // cache keeps its first registration, and the singletons are added once.
    private static void AddMessagingConfigCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
    {
        services.Configure<VaultConfigurationOptions>(configuration.GetSection(sectionName));

        services.AddStellaegisBoundedCache(VaultConfigurationOptions.ConfigCacheKey, sp =>
        {
            var options = sp.GetRequiredService<IOptions<VaultConfigurationOptions>>().Value;
            return new BoundedCacheOptions
            {
                SizeLimit = options.ConfigCacheSizeLimit,
                Expiration = options.ConfigCacheTtl,
            };
        });

        services.TryAddSingleton<MessagingConfigCache>();
        services.TryAddSingleton<IMessagingConfigCache>(sp => sp.GetRequiredService<MessagingConfigCache>());
    }
}
