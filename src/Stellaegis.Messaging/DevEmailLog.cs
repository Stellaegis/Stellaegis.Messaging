using Microsoft.Extensions.Logging;

namespace Stellaegis.Messaging;

/// <summary>
/// Renders an <see cref="EmailMessage"/> to the process output instead of sending it.
/// Shared by <see cref="LoggingEmailSender"/> (the zero-config dev sender) and by
/// <c>MessagingEmailSender</c>'s degrade path (no relay configured yet). Sending a real
/// email before the relay is configured is the greater risk, so the safe default is to show it.
/// </summary>
internal static class DevEmailLog
{
    private const string Border = "══════════════════════════════════════════════════════════";

    /// <summary>Writes the message to the console (readable box) and the logger (structured line).</summary>
    /// <param name="logger">Logger for the structured "email not sent" line.</param>
    /// <param name="message">The email that would have been sent.</param>
    /// <param name="reason">Short banner note, e.g. "dev — no SMTP configured" or "no relay configured".</param>
    public static void Write(ILogger logger, EmailMessage message, string reason)
    {
        var body = message.TextBody ?? StripHtml(message.HtmlBody);
        var to = string.Join(", ", message.To.Select(t => t.Address));
        var prev = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine($"  ╔{Border}╗");
        Console.WriteLine($"  ║  📧  EMAIL ({reason})");
        Console.WriteLine($"  ╠{Border}╣");
        Console.WriteLine($"  ║  To      : {to}");
        Console.WriteLine($"  ║  Subject : {message.Subject}");
        Console.WriteLine($"  ╠{Border}╣");
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Console.WriteLine($"  {line.Trim()}");
        Console.WriteLine($"  ╚{Border}╝");
        Console.WriteLine();
        Console.ForegroundColor = prev;

        logger.LogInformation("Email not sent ({Reason}) — To: {To}, Subject: {Subject}",
            reason, to, message.Subject);
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty).Trim();
    }
}
