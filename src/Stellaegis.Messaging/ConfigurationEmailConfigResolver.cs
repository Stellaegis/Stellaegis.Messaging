using Microsoft.Extensions.Configuration;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves the configuration of a space from a configuration section, read against the frame of the application.
/// </summary>
/// <remarks>
/// <para>
/// In an application with a single compartment, <c>{section}:Configuration</c> is the configuration of that
/// compartment: the application names no space in its configuration, and turns nothing on.
/// </para>
/// <para>
/// In an application with several compartments, <c>{section}:Configuration</c> is the configuration of the
/// shared space, and <c>{section}:Compartments:{compartment}</c> that of a compartment, when the file holds one.
/// A compartment without a configuration of its own reaches the shared one only when
/// <see cref="MessagingModule.InheritSharedSpace"/> is turned on.
/// </para>
/// </remarks>
internal sealed class ConfigurationEmailConfigResolver(
    IConfigurationSection section,
    ApplicationFrame frame,
    bool inheritSharedSpace) : IEmailConfigResolver
{
    /// <summary>Default configuration section name, holding <see cref="ConfigurationKey"/> and <see cref="CompartmentsKey"/>.</summary>
    public const string SectionName = "Stellaegis:Messaging";

    /// <summary>The key of the configuration of the single compartment, or of the shared space.</summary>
    public const string ConfigurationKey = "Configuration";

    /// <summary>The key under which each compartment of an application with several may have its configuration.</summary>
    public const string CompartmentsKey = "Compartments";

    /// <inheritdoc />
    public Task<MessagingConfig?> ResolveAsync(Space space, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(space);
        return Task.FromResult(Resolve(space));
    }

    private MessagingConfig? Resolve(Space space)
    {
        if (frame.Compartments == CompartmentCount.Single)
        {
            // The section describes the single compartment. The shared space, when the frame declares one, is not
            // described here: its messages are written to the log.
            return space.Compartment == frame.FixedCompartment ? Read(section.GetSection(ConfigurationKey)) : null;
        }

        if (space.Compartment is { } compartment)
        {
            var own = Read(section.GetSection(CompartmentsKey).GetSection(compartment.Value));
            if (own is not null || !inheritSharedSpace)
                return own;
        }

        return Read(section.GetSection(ConfigurationKey));
    }

    // Bound at each call: a reloaded configuration source takes effect on the next send.
    private static MessagingConfig? Read(IConfigurationSection configuration)
    {
        var config = configuration.Exists() ? configuration.Get<MessagingConfig>() : null;
        return config is { Transports.Count: > 0 } ? config : null;
    }
}
