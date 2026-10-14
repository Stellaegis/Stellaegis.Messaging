using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Backplane;
using Stellaegis.Backplane.SecretEvents;
using Stellaegis.Caching;
using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging.Backplane;

/// <summary>
/// Backplane integration for the messaging configuration cache.
/// </summary>
public static class MessagingBackplaneExtensions
{
    /// <summary>
    /// Subscribes to <c>SecretChangedEvent</c> and evicts the cached messaging configuration for the
    /// changed vault path, so an edited relay configuration takes effect on the next send without waiting
    /// for the cache TTL. Call once at startup after the backplane transport is ready, in every host that
    /// sends mail. Requires <c>AddStellaegisSecretsBackplaneEvents()</c> and
    /// <c>AddStellaegisEmailConfigResolver()</c> (both wired by <c>AddStellaegisEmail</c>).
    /// </summary>
    public static void UseStellaegisMessagingSecretsBackplane(this IServiceProvider services)
    {
        var backplane = services.GetRequiredService<IBackplane>();
        var invalidator = services.GetRequiredKeyedService<CacheSegmentInvalidator>(MessagingOptions.SecretCacheKey);

        backplane.Subscribe<SecretChangedEvent>((evt, _) =>
        {
            invalidator.Invalidate(evt.Path);
            return Task.CompletedTask;
        });
    }
}
