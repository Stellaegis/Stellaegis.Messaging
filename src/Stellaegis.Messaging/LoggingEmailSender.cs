using Microsoft.Extensions.Logging;

namespace Stellaegis.Messaging;

/// <summary>
/// Development-only <see cref="IEmailSender"/> that prints email content
/// to the process output instead of sending via SMTP. Useful when no mail server is configured.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        DevEmailLog.Write(logger, message, "dev — no SMTP configured");
        return Task.CompletedTask;
    }

    public Task SendAsync(string tenantSlug, EmailMessage message, CancellationToken cancellationToken = default)
        => SendAsync(message, cancellationToken);
}
