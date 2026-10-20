using Microsoft.Extensions.Configuration;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves the configuration of the shared space from a configuration section, and that of a compartment
/// only when <see cref="MessagingModule.InheritSharedSpace"/> is turned on.
/// </summary>
internal sealed class ConfigurationEmailConfigResolver(
    IConfigurationSection section,
    bool inheritSharedSpace) : IEmailConfigResolver
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging:Configuration";

    /// <inheritdoc />
    public Task<MessagingConfig?> ResolveAsync(Space space, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(space);

        if (!space.IsShared && !inheritSharedSpace)
            return Task.FromResult<MessagingConfig?>(null);

        // Bound at each call: a reloaded configuration source takes effect on the next send.
        var config = section.Exists() ? section.Get<MessagingConfig>() : null;
        return Task.FromResult(config is { Transports.Count: > 0 } ? config : null);
    }
}
