using System.Security.Cryptography.X509Certificates;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging.MailKit;

/// <summary>
/// SMTP implementation of <see cref="IEmailTransport"/> using MailKit.
/// Receives an already-resolved <see cref="ResolvedTransport"/> per send (no static options),
/// supports Basic and client-certificate authentication, and sets an explicit envelope sender
/// (MAIL FROM / Return-Path) when configured.
/// </summary>
/// <remarks>
/// A new SMTP connection is opened per send — appropriate for transactional email.
/// On transient failure, up to 2 retries are attempted (1 s, then 2 s). The message From/Reply-To/Sender
/// are already finalized by the orchestrating <see cref="IEmailSender"/>.
/// </remarks>
internal sealed class MailKitEmailTransport(ILogger<MailKitEmailTransport> logger) : IEmailTransport
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    public async Task SendAsync(
        ResolvedTransport transport,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(transport);
        Guard.NotNull(message);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await SendOnceAsync(transport, message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                logger.LogWarning(ex,
                    "SMTP send attempt {Attempt} failed via {Host}:{Port} — Subject: {Subject}. Retrying in {Delay}s.",
                    attempt + 1, transport.Host, transport.Port, message.Subject, RetryDelays[attempt].TotalSeconds);
                await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendOnceAsync(
        ResolvedTransport transport,
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transport.Host))
            throw new InvalidOperationException("SMTP transport host is not configured.");

        var mime = BuildMimeMessage(message);

        using var smtp = new SmtpClient();

        if (transport.Auth == TransportAuth.ClientCert && LoadClientCertificate(transport.Credentials) is { } certificate)
            smtp.ClientCertificates = new X509Certificate2Collection(certificate);

        await smtp.ConnectAsync(transport.Host, transport.Port, MapSecurity(transport.Security), cancellationToken)
            .ConfigureAwait(false);

        if (transport.Auth == TransportAuth.Basic
            && transport.Credentials is { Username: { Length: > 0 } username } credentials)
        {
            var password = credentials.Password
                ?? throw new InvalidOperationException("SMTP Basic auth username is set but no password is configured.");
            await smtp.AuthenticateAsync(username, password, cancellationToken).ConfigureAwait(false);
        }

        // Explicit envelope sender (MAIL FROM / Return-Path) — what SPF checks; belongs to the relay.
        if (!string.IsNullOrWhiteSpace(transport.EnvelopeFrom))
        {
            var envelopeSender = MailboxAddress.Parse(transport.EnvelopeFrom);
            var recipients = mime.To.Mailboxes
                .Concat(mime.Cc.Mailboxes)
                .Concat(mime.Bcc.Mailboxes);
            await smtp.SendAsync(mime, envelopeSender, recipients, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await smtp.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        }

        await smtp.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    internal static MimeMessage BuildMimeMessage(EmailMessage message)
    {
        if (message.From is null)
            throw new InvalidOperationException("EmailMessage has no From address (the sender engine must resolve it).");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.From.Name, message.From.Address));

        if (message.Sender is not null)
            mime.Sender = new MailboxAddress(message.Sender.Name, message.Sender.Address);

        AddAll(mime.To, message.To);
        AddAll(mime.Cc, message.Cc);
        AddAll(mime.Bcc, message.Bcc);
        AddAll(mime.ReplyTo, message.ReplyTo);

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        }.ToMessageBody();
        return mime;
    }

    private static void AddAll(InternetAddressList list, IReadOnlyList<EmailAddress> addresses)
    {
        foreach (var address in addresses)
            list.Add(new MailboxAddress(address.Name, address.Address));
    }

    internal static SecureSocketOptions MapSecurity(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls
    };

    internal static X509Certificate2? LoadClientCertificate(TransportCredentials? credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.CertificatePfxBase64))
            return null;

        var raw = Convert.FromBase64String(credentials.CertificatePfxBase64);
        return X509CertificateLoader.LoadPkcs12(raw, credentials.CertificatePassword);
    }
}
