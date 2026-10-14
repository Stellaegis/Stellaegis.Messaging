using Stellaegis.Primitives;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Vault paths of the messaging configuration, one per space: <c>messaging.{compartment}</c>, and
/// <c>messaging._shared</c> for the shared space.
/// </summary>
internal static class MessagingSecretPaths
{
    private const string Root = "messaging";

    // A valid SecretPath segment that no Compartment can be: the grammar of a compartment refuses '_'.
    // A readable name such as "shared" or "default" is a valid compartment, which would then read and
    // overwrite the shared configuration.
    private const string SharedSegment = "_shared";

    /// <summary>Path of the configuration of <paramref name="space"/>.</summary>
    public static SecretPath For(Space space)
        => space.IsShared
            ? SecretPath.From(Root, SharedSegment)
            : SecretPath.From(Root, space.Compartment.Value);
}
