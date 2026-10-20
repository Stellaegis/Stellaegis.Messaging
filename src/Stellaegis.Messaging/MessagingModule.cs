using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// The identity of the messaging module, <c>messaging</c>: the vault paths of its configuration carry it.
/// </summary>
public sealed class MessagingModule : IStellaegisModule
{
    /// <inheritdoc />
    public static string Name => "messaging";
}
