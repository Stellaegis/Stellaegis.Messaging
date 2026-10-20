using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Stellaegis.Messaging;

/// <summary>
/// Fails the start of the host when the engine is registered without a configuration source or without a
/// transport, naming the registration to add. Without it, the first send fails, and the outbox worker fails at
/// every cycle without ever stopping.
/// </summary>
/// <remarks>
/// Registered before the outbox worker, so that the worker never starts on an incomplete assembly. Checks
/// registrations only: nothing is resolved, so a scoped service is checked as well.
/// </remarks>
internal sealed class MessagingStartupCheck(IServiceProviderIsService services) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();

        if (!services.IsService(typeof(IEmailConfigResolver)))
            missing.Add(
                "no configuration source: call AddStellaegisEmailConfiguration for a configuration section, " +
                "or AddStellaegisEmailVaultConfiguration (Stellaegis.Messaging.Secrets) for a vault");

        if (!services.IsService(typeof(IEmailTransport)))
            missing.Add("no transport: call AddStellaegisMailKitTransport (Stellaegis.Messaging.MailKit) for SMTP");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Stellaegis.Messaging cannot start: {string.Join("; ", missing)}.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
