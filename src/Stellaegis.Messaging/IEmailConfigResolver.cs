using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves the messaging configuration of a space, reading the raw vault blob (credentials included)
/// with caching. Used by the sending engine — not by editing surfaces, which go through
/// <see cref="IMessagingConfigStore"/> and get a masked projection.
/// </summary>
/// <remarks>
/// A compartment reads <c>messaging.{compartment}</c>, the shared space <c>messaging._shared</c>. A
/// compartment without a configuration of its own falls back to the shared space only when
/// <see cref="MessagingOptions.InheritSharedSpace"/> is set.
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
