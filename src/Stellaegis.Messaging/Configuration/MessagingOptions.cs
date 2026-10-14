namespace Stellaegis.Messaging.Configuration;

/// <summary>
/// Options of the messaging engine. Bind from the <c>Stellaegis:Messaging</c> configuration section.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging";

    /// <summary>
    /// Whether a compartment without a configuration of its own uses the configuration of the shared space.
    /// Off by default: a compartment then reaches the shared configuration only when the application says
    /// so, and a compartment without a relay behaves as "no relay configured" — its messages are written
    /// to the log, never sent.
    /// </summary>
    public bool InheritSharedSpace { get; set; }
}
