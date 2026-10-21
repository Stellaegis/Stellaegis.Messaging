using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// The identity of the messaging module, <c>messaging</c>: the vault paths of its configuration carry it, and so
/// does the schema of its outbox.
/// </summary>
public sealed class MessagingModule : IStellaegisModule
{
    /// <inheritdoc />
    public static string Name => "messaging";

    /// <summary>
    /// A compartment without a configuration of its own uses the configuration of the shared space. The application
    /// turns it on, <c>services.AddStellaegisModuleParameter&lt;MessagingModule&gt;(InheritSharedSpace)</c>; left off,
    /// a compartment without a relay of its own behaves as "no relay configured" — its messages are written to the
    /// log, never sent. The capability supposes a shared space: turned on in an application whose frame declares
    /// none, it fails the startup.
    /// </summary>
    public static ModuleCapability InheritSharedSpace { get; } = new("inherit_shared_space", requiresSharedSpace: true);
}
