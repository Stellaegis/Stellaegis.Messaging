using Stellaegis.Secrets;

namespace Stellaegis.Messaging;

/// <summary>
/// Evicts resolved messaging configuration from the cache, so that an edit takes effect on the next send
/// rather than at the end of the cache TTL.
/// </summary>
/// <remarks>
/// The module subscribes to nothing. Edits made through <see cref="IMessagingConfigStore"/> evict in the
/// process that makes them. A host that learns of a secret changed elsewhere — from its message bus, for
/// instance — calls <see cref="Invalidate"/> with the path of that secret.
/// </remarks>
public interface IMessagingConfigCache
{
    /// <summary>
    /// Evicts every cached configuration read from <paramref name="path"/>, or that a secret at that path
    /// would replace. A path outside the messaging configuration evicts nothing.
    /// </summary>
    void Invalidate(SecretPath path);
}
