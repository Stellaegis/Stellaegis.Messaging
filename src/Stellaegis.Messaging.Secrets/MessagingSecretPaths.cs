using Stellaegis.Primitives;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Vault paths of the messaging configuration, one per space: <c>{compartment}.messaging.configuration</c>, and
/// <c>_shared.messaging.configuration</c> for the shared space.
/// </summary>
internal static class MessagingSecretPaths
{
    private const string Name = "configuration";

    /// <summary>Path of the configuration of <paramref name="space"/>.</summary>
    public static SecretPath For(Space space) => SecretPath.For<MessagingModule>(space, Name);
}
