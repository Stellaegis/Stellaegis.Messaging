using Microsoft.Extensions.Logging;

namespace Stellaegis.Messaging;

/// <summary>
/// Writes an <see cref="EmailMessage"/> to the log instead of sending it. Shared by <see cref="LoggingEmailSender"/>
/// (the zero-config dev sender) and by <c>MessagingEmailSender</c>'s degrade path (no relay configured yet).
/// Sending a real email before the relay is configured is the greater risk, so the safe default is to show it.
/// </summary>
/// <remarks>
/// The log is the only output: a host chooses where its log goes — the console during development —, and a library
/// writes to nothing else.
/// </remarks>
internal static class DevEmailLog
{
    /// <summary>Writes the message to the logger: one structured entry, the body as its last property.</summary>
    /// <param name="logger">Logger for the "email not sent" entry.</param>
    /// <param name="message">The email that would have been sent.</param>
    /// <param name="reason">Short note, e.g. "dev — no SMTP configured" or "no relay configured".</param>
    public static void Write(ILogger logger, EmailMessage message, string reason)
    {
        var body = message.TextBody ?? StripHtml(message.HtmlBody);
        var to = string.Join(", ", message.To.Select(t => t.Address));

        logger.LogInformation(
            "Email not sent ({Reason}) — To: {To}, Subject: {Subject}\n{Body}",
            reason, to, message.Subject, body);
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty).Trim();
    }
}
