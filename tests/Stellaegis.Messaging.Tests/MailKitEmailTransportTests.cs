using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.MailKit;

namespace Stellaegis.Messaging.Tests;

public sealed class MailKitEmailTransportTests
{
    private static EmailMessage Message() => new()
    {
        From = new EmailAddress("noreply@example.test", "Example"),
        Sender = new EmailAddress("agent@example.test"),
        To = [new EmailAddress("to@example.test", "To")],
        Cc = [new EmailAddress("cc@example.test")],
        Bcc = [new EmailAddress("bcc@example.test")],
        ReplyTo = [new EmailAddress("reply@example.test")],
        Subject = "Hello",
        HtmlBody = "<p>Hi</p>",
        TextBody = "Hi",
    };

    private static string Addresses(InternetAddressList list)
        => string.Join(",", list.Mailboxes.Select(m => m.Address));

    [Fact]
    public void Mime_message_carries_every_address_and_both_bodies()
    {
        var mime = MailKitEmailTransport.BuildMimeMessage(Message());

        Assert.Equal("noreply@example.test", Addresses(mime.From));
        Assert.Equal("Example", mime.From.Mailboxes.Single().Name);
        Assert.Equal("agent@example.test", Assert.IsType<MailboxAddress>(mime.Sender).Address);
        Assert.Equal("to@example.test", Addresses(mime.To));
        Assert.Equal("cc@example.test", Addresses(mime.Cc));
        Assert.Equal("bcc@example.test", Addresses(mime.Bcc));
        Assert.Equal("reply@example.test", Addresses(mime.ReplyTo));
        Assert.Equal("Hello", mime.Subject);
        Assert.Equal("<p>Hi</p>", mime.HtmlBody);
        Assert.Equal("Hi", mime.TextBody);
    }

    [Fact]
    public void Mime_message_without_text_body_is_html_only()
    {
        var mime = MailKitEmailTransport.BuildMimeMessage(Message() with { TextBody = null, Sender = null });

        Assert.Equal("<p>Hi</p>", mime.HtmlBody);
        Assert.Null(mime.TextBody);
        Assert.Null(mime.Sender);
    }

    [Fact]
    public void Message_without_from_is_refused()
    {
        Assert.Throws<InvalidOperationException>(
            () => MailKitEmailTransport.BuildMimeMessage(Message() with { From = null }));
    }

    [Theory]
    [InlineData(SmtpSecurity.None, SecureSocketOptions.None)]
    [InlineData(SmtpSecurity.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(SmtpSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
    public void Security_mode_maps_to_its_mailkit_option(SmtpSecurity security, SecureSocketOptions expected)
    {
        Assert.Equal(expected, MailKitEmailTransport.MapSecurity(security));
    }

    [Fact]
    public void Client_certificate_loads_from_a_password_protected_pfx()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=relay-client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var credentials = new TransportCredentials
        {
            CertificatePfxBase64 = Convert.ToBase64String(issued.Export(X509ContentType.Pfx, "pfx-password")),
            CertificatePassword = "pfx-password",
        };

        using var loaded = MailKitEmailTransport.LoadClientCertificate(credentials);

        Assert.NotNull(loaded);
        Assert.Equal(issued.Thumbprint, loaded.Thumbprint);
        Assert.True(loaded.HasPrivateKey);
    }

    [Fact]
    public void No_certificate_configured_loads_nothing()
    {
        Assert.Null(MailKitEmailTransport.LoadClientCertificate(new TransportCredentials { Username = "user" }));
        Assert.Null(MailKitEmailTransport.LoadClientCertificate(null));
        Assert.Null(new MailKitEmailTransport(NullLogger<MailKitEmailTransport>.Instance).ClientCertificateOf(null));
    }

    [Fact]
    public void Client_certificate_is_loaded_once_per_pfx_and_kept()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=relay-client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Convert.ToBase64String(issued.Export(X509ContentType.Pfx, "pfx-password"));
        var transport = new MailKitEmailTransport(NullLogger<MailKitEmailTransport>.Instance);

        var first = transport.ClientCertificateOf(new TransportCredentials { CertificatePfxBase64 = pfx, CertificatePassword = "pfx-password" });
        var again = transport.ClientCertificateOf(new TransportCredentials { CertificatePfxBase64 = pfx, CertificatePassword = "pfx-password" });

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal(issued.Thumbprint, first.Thumbprint);
    }

    [Fact]
    public void Registration_provides_the_mailkit_transport()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddStellaegisMailKitTransport();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MailKitEmailTransport>(provider.GetRequiredService<IEmailTransport>());
    }
}
