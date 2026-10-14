namespace Stellaegis.Messaging;

/// <summary>
/// Sends transactional email messages.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends the given <paramref name="message"/> asynchronously, resolving configuration
    /// from the current tenant context.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the given <paramref name="message"/> asynchronously for an explicit tenant.
    /// Use this overload from background workers that have no ambient tenant context.
    /// </summary>
    Task SendAsync(string tenantSlug, EmailMessage message, CancellationToken cancellationToken = default);
}
