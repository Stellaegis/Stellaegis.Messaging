using Microsoft.Extensions.Logging;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Development-only <see cref="IEmailSender"/> that prints email content
/// to the process output instead of sending via SMTP. Useful when no mail server is configured.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(Space space, EmailMessage message, CancellationToken cancellationToken = default)
    {
        DevEmailLog.Write(logger, message, "dev — no SMTP configured");
        return Task.CompletedTask;
    }
}
