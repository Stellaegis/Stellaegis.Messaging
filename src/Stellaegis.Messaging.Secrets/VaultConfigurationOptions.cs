namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Cache options of the messaging configuration read from the vault. Bind from the
/// <c>Stellaegis:Messaging</c> configuration section.
/// </summary>
public sealed class VaultConfigurationOptions
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
}
