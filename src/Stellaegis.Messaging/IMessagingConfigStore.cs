using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging;

/// <summary>
/// Read/write surface for messaging configuration, backing the admin and CLI editing surfaces.
/// Unlike <see cref="IEmailConfigResolver"/> (which reads the raw blob for the engine), the store
/// returns a <b>masked</b> projection (secret credential fields blanked) on read and <b>merges</b>
/// masked fields with the stored values on write — so an editing surface never reads a live secret
/// and cannot accidentally overwrite one it never saw.
/// </summary>
public interface IMessagingConfigStore
{
    /// <summary>
    /// Reads the configuration for a perimeter (<c>"default"</c> or a tenant slug) with sensitive
    /// credential fields masked. Returns <see langword="null"/> when the scope is not configured.
    /// </summary>
    Task<MessagingConfig?> GetAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the configuration for a perimeter, merging masked/blank credential fields with the
    /// currently stored values (a masked field keeps its stored secret).
    /// </summary>
    Task SaveAsync(string scope, MessagingConfig config, CancellationToken cancellationToken = default);

    /// <summary>Deletes the configuration for a perimeter. No-op when absent.</summary>
    Task DeleteAsync(string scope, CancellationToken cancellationToken = default);
}
