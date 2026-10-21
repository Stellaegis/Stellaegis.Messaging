using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Caching;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// The bounded cache of resolved configurations, keyed by space. Each entry is attached to the vault paths
/// it depends on, so that invalidating a path evicts it.
/// </summary>
internal sealed class MessagingConfigCache(
    [FromKeyedServices(VaultConfigurationOptions.ConfigCacheKey)] BoundedCache cache) : IMessagingConfigCache
{
    private readonly CacheSegmentInvalidator _segments = new();

    public bool TryGet(Space space, [MaybeNullWhen(false)] out MessagingConfig config)
        => cache.TryGet(Key(space), out config);

    public void Set(Space space, MessagingConfig config, IEnumerable<SecretPath> sources, TimeSpan ttl)
        => cache.Set(Key(space), config, sources.Select(path => _segments.GetToken(path.ToString())).ToList(), ttl);

    /// <inheritdoc />
    public void Invalidate(SecretPath path) => _segments.Invalidate(Guard.NotNull(path).ToString());

    // The key is the space itself, not the path of its secret: the cache holds one configuration per space,
    // whichever path answered for it, and the paths are what the entry is attached to.
    private static string Key(Space space) => space.ToSegment();
}
