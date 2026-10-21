using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
/// are already finalized by the orchestrating <see cref="IEmailSender"/>. A client certificate is loaded once
/// per distinct PFX and kept for the next sends; a rotated certificate is a new entry.
/// </remarks>
internal sealed class MailKitEmailTransport(ILogger<MailKitEmailTransport> logger) : IEmailTransport
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    // Bounded by clearing: the certificates of a process are few, and a rotation leaves its predecessor behind.
    private const int MaxCachedCertificates = 64;
    private readonly ConcurrentDictionary<string, X509Certificate2> _certificates = new(StringComparer.Ordinal);

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

        if (transport.Auth == TransportAuth.ClientCert && ClientCertificateOf(transport.Credentials) is { } certificate)
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

    /// <summary>
    /// The client certificate of <paramref name="credentials"/>, loaded once per distinct PFX and password and
    /// kept for the next sends, or <see langword="null"/> when the credentials hold none.
    /// </summary>
    internal X509Certificate2? ClientCertificateOf(TransportCredentials? credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.CertificatePfxBase64))
            return null;

        if (_certificates.Count >= MaxCachedCertificates)
            _certificates.Clear();

        return _certificates.GetOrAdd(KeyOf(credentials), _ => LoadClientCertificate(credentials)!);
    }

    internal static X509Certificate2? LoadClientCertificate(TransportCredentials? credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials?.CertificatePfxBase64))
            return null;

        var raw = Convert.FromBase64String(credentials.CertificatePfxBase64);
        return X509CertificateLoader.LoadPkcs12(raw, credentials.CertificatePassword);
    }

    // A digest of the PFX and its password, so that the cache holds no secret as a key.
    private static string KeyOf(TransportCredentials credentials)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.CertificatePfxBase64 + "\0" + credentials.CertificatePassword)));
}
