using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Read/write surface for messaging configuration, backing editing surfaces such as an administration
/// page or a command-line tool. Unlike <see cref="IEmailConfigResolver"/> (which reads the raw blob for the
/// engine), the store returns a <b>masked</b> projection (secret credential fields blanked) on read and
/// <b>merges</b> masked fields with the stored values on write — so an editing surface never reads a live
/// secret and cannot accidentally overwrite one it never saw.
/// </summary>
/// <remarks>
/// Each space has its own configuration; the store never falls back to the shared space. Saving or
/// deleting evicts the cached configuration in this process.
/// </remarks>
public interface IMessagingConfigStore
{
    /// <summary>
    /// Reads the configuration of <paramref name="space"/> with sensitive credential fields masked.
    /// Returns <see langword="null"/> when the space is not configured.
    /// </summary>
    Task<MessagingConfig?> GetAsync(Space space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the configuration of <paramref name="space"/>, merging masked/blank credential fields with the
    /// currently stored values (a masked field keeps its stored secret).
    /// </summary>
    Task SaveAsync(Space space, MessagingConfig config, CancellationToken cancellationToken = default);

    /// <summary>Deletes the configuration of <paramref name="space"/>. No-op when absent.</summary>
    Task DeleteAsync(Space space, CancellationToken cancellationToken = default);
}
