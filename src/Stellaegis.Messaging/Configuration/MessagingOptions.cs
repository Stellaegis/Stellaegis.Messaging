namespace Stellaegis.Messaging.Configuration;

/// <summary>
/// Installation-level options for the messaging config resolver.
/// Bind from the <c>Stellaegis:Messaging</c> configuration section.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging";

    /// <summary>Service key of the bounded cache holding resolved messaging configuration.</summary>
    public const string SecretCacheKey = "messaging-config";

    /// <summary>
    /// TTL for resolved configuration cached in memory (default: 1 hour). A long TTL is safe: a config
    /// edit through the vault publishes a <c>secret.changed</c> event that evicts the affected entry
    /// immediately (see <c>UseStellaegisMessagingSecretsBackplane</c>). The TTL is only a backstop for
    /// hosts that do not wire the backplane subscription.
    /// </summary>
    public TimeSpan SecretCacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of resolved configurations held in memory. The key space grows with the number of
    /// perimeters (tenants + system default), so the cache is bounded to cap memory. When
    /// <see langword="null"/> the bound is auto-derived from available memory at startup.
    /// </summary>
    public long? SecretCacheSizeLimit { get; set; }
}
