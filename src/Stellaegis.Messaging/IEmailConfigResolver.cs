using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves the messaging configuration of a space, credentials included. Used by the sending engine —
/// not by editing surfaces, which go through <see cref="IMessagingConfigStore"/> and get a masked
/// projection.
/// </summary>
/// <remarks>
/// Implemented on a configuration section (<c>AddStellaegisEmailConfiguration</c>) and on a vault
/// (<c>Stellaegis.Messaging.Secrets</c>). A compartment without a configuration of its own falls back to
/// the shared space only when <see cref="MessagingModule.InheritSharedSpace"/> is turned on.
/// </remarks>
public interface IEmailConfigResolver
{
    /// <summary>
    /// Resolves the effective messaging configuration of <paramref name="space"/>, or <c>null</c> when
    /// none applies. A <c>null</c> result is the "no relay configured yet" state: the sender degrades to
    /// logging rather than sending — sending a real email before the relay is configured is the greater risk.
    /// </summary>
    Task<MessagingConfig?> ResolveAsync(Space space, CancellationToken cancellationToken = default);
}
