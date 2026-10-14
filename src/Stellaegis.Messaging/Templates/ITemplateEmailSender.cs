namespace Stellaegis.Messaging.Templates;

/// <summary>
/// Loads a system template, renders it with Scriban, and sends the resulting email.
/// </summary>
public interface ITemplateEmailSender
{
    /// <summary>
    /// Renders the named template with the supplied data and sends the email.
    /// </summary>
    /// <param name="templateName">Template path, e.g. <c>identity/email-verification</c>.</param>
    /// <param name="data">Anonymous object whose properties become Scriban variables.</param>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="toName">Optional recipient display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(
        string templateName,
        object data,
        string to,
        string subject,
        string? toName = null,
        CancellationToken cancellationToken = default);
}
