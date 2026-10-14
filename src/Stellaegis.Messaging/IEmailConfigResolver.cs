using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves the messaging configuration for a perimeter, reading the raw vault blob
/// (credentials included) with caching. Used by the sending engine — not by editing surfaces,
/// which go through <c>IMessagingConfigStore</c> and get a masked projection.
/// </summary>
/// <remarks>
/// Resolution follows <c>messaging.{tenantSlug}</c> → fallback <c>messaging.default</c> (system scope).
/// The <c>tenantSlug</c> is explicit so background workers (no HTTP tenant context) can resolve too.
/// </remarks>
public interface IEmailConfigResolver
{
    /// <summary>
    /// Resolves the effective messaging configuration for the given tenant, or <c>null</c> when
    /// neither the tenant scope nor the system scope (<c>messaging.default</c>) is configured.
    /// A <c>null</c> result is the "no relay configured yet" state: the sender degrades to logging
    /// rather than sending — sending a real email before the relay is configured is the greater risk.
    /// </summary>
    Task<MessagingConfig?> ResolveAsync(string tenantSlug, CancellationToken cancellationToken = default);
}
