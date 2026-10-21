using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Cache options of the messaging configuration read from the vault. Bind from the
/// <c>Stellaegis:Messaging</c> configuration section; validated when the host starts.
/// </summary>
public sealed class VaultConfigurationOptions : IValidatableOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging";

    /// <summary>Service key of the bounded cache holding resolved messaging configuration.</summary>
    public const string ConfigCacheKey = "messaging-config";

    /// <summary>
    /// TTL for resolved configuration cached in memory (default: 1 hour). A long TTL is safe as long as
    /// changes are evicted: edits made through <c>IMessagingConfigStore</c> evict in this process, and a
    /// host evicts edits made elsewhere through <see cref="IMessagingConfigCache"/>. The TTL is the
    /// backstop when an eviction is missed.
    /// </summary>
    public TimeSpan ConfigCacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of resolved configurations held in memory. The key space grows with the number of
    /// compartments, so the cache is bounded to cap memory. When <see langword="null"/> the bound is
    /// derived from available memory at startup.
    /// </summary>
    public long? ConfigCacheSizeLimit { get; set; }

    /// <inheritdoc />
    public string? FindError()
    {
        if (ConfigCacheTtl <= TimeSpan.Zero)
            return $"ConfigCacheTtl is {ConfigCacheTtl}: it must be positive.";
        if (ConfigCacheSizeLimit is { } limit && limit < 1)
            return $"ConfigCacheSizeLimit is {limit}: it must be positive, or left unset to derive the bound from memory.";
        return null;
    }
}
