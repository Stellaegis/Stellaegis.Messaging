using Microsoft.Extensions.Logging.Abstractions;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;
using static Stellaegis.Messaging.Tests.Spaces;

namespace Stellaegis.Messaging.Tests;

public sealed class MessagingEmailSenderTests
{
    private static readonly EmailAddress Fallback = new("noreply@example.test", "Example");

    private static EmailMessage Message(EmailAddress? from = null, EmailAddress? sender = null) => new()
    {
        From = from,
        Sender = sender,
        To = [new EmailAddress("dest@example.test")],
        Subject = "Hello",
        HtmlBody = "<p>Hi</p>",
    };

    private static MessagingConfig Config(TransportConfig transport, params FromAddress[] fromAddresses) => new()
    {
        Transports = new Dictionary<string, TransportConfig> { ["default"] = transport },
        FromAddresses = fromAddresses,
    };

    private static TransportConfig Transport(
        FromPolicyKind kind,
        FromMismatchBehavior onMismatch = FromMismatchBehavior.Degrade,
        params string[] domains) => new()
    {
        Host = "smtp.example.test",
        DefaultFrom = Fallback,
        FromPolicy = new FromPolicy { Kind = kind, Domains = domains },
        OnFromMismatch = onMismatch,
    };

    private static (MessagingEmailSender Sender, RecordingOutbox Outbox) Build(MessagingConfig? config)
    {
        var outbox = new RecordingOutbox();
        var sender = new MessagingEmailSender(
            new FakeEmailConfigResolver(config), outbox, NullLogger<MessagingEmailSender>.Instance);
        return (sender, outbox);
    }

    [Fact]
    public async Task Unrestricted_relay_keeps_the_desired_from()
    {
        var (sender, outbox) = Build(Config(Transport(FromPolicyKind.IpUnrestricted)));

        await sender.SendAsync(Acme, Message(new EmailAddress("sales@other.test", "Sales")));

        var (space, message, transport) = Assert.Single(outbox.Enqueued);
        Assert.Equal(Acme, space);
        Assert.Equal("default", transport);
        Assert.Equal(new EmailAddress("sales@other.test", "Sales"), message.From);
        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public async Task Message_without_from_takes_the_transport_default()
    {
        var (sender, outbox) = Build(Config(Transport(FromPolicyKind.IpUnrestricted)));

        await sender.SendAsync(Acme, Message());

        Assert.Equal(Fallback, Assert.Single(outbox.Enqueued).Message.From);
    }

    [Fact]
    public async Task Authorized_domain_keeps_the_from_and_drops_a_sender_outside_it()
    {
        var (sender, outbox) = Build(Config(
            Transport(FromPolicyKind.DomainAuthorized, domains: "example.test")));

        await sender.SendAsync(Acme, Message(
            new EmailAddress("Sales@EXAMPLE.test"), sender: new EmailAddress("agent@other.test")));

        var message = Assert.Single(outbox.Enqueued).Message;
        Assert.Equal("Sales@EXAMPLE.test", message.From!.Address);
        Assert.Null(message.Sender);
    }

    [Fact]
    public async Task Unauthorized_from_degrades_to_the_default_and_moves_to_reply_to()
    {
        var (sender, outbox) = Build(Config(
            Transport(FromPolicyKind.DomainAuthorized, domains: "example.test")));

        await sender.SendAsync(Acme, Message(new EmailAddress("jane@other.test", "Jane")));

        var message = Assert.Single(outbox.Enqueued).Message;
        Assert.Equal(new EmailAddress(Fallback.Address, "Jane"), message.From);
        Assert.Equal("jane@other.test", Assert.Single(message.ReplyTo).Address);
    }

    [Fact]
    public async Task Unauthorized_from_is_rejected_when_the_transport_says_so()
    {
        var (sender, outbox) = Build(Config(
            Transport(FromPolicyKind.DomainAuthorized, FromMismatchBehavior.Reject, "example.test")));

        await Assert.ThrowsAsync<EmailRejectedException>(
            () => sender.SendAsync(Acme, Message(new EmailAddress("jane@other.test"))));

        Assert.Empty(outbox.Enqueued);
    }

    [Fact]
    public async Task Mailbox_bound_relay_forces_its_mailbox_and_speaks_for_no_one_else()
    {
        var (sender, outbox) = Build(Config(Transport(FromPolicyKind.MailboxBound)));

        await sender.SendAsync(Acme, Message(
            new EmailAddress("jane@example.test", "Jane"), sender: new EmailAddress("agent@example.test")));

        var message = Assert.Single(outbox.Enqueued).Message;
        Assert.Equal(new EmailAddress(Fallback.Address, "Jane"), message.From);
        Assert.Equal("jane@example.test", Assert.Single(message.ReplyTo).Address);
        Assert.Null(message.Sender);
    }

    [Fact]
    public async Task Declared_address_selects_its_transport_and_reply_to()
    {
        var config = new MessagingConfig
        {
            Transports = new Dictionary<string, TransportConfig>
            {
                ["default"] = Transport(FromPolicyKind.IpUnrestricted),
                ["billing"] = Transport(FromPolicyKind.IpUnrestricted),
            },
            FromAddresses = [new FromAddress { Address = "billing@example.test", Transport = "billing", ReplyTo = "accounts@example.test" }],
        };
        var (sender, outbox) = Build(config);

        await sender.SendAsync(Acme, Message(new EmailAddress("billing@example.test")));

        var (_, message, transport) = Assert.Single(outbox.Enqueued);
        Assert.Equal("billing", transport);
        Assert.Equal("accounts@example.test", Assert.Single(message.ReplyTo).Address);
    }

    [Fact]
    public async Task Unknown_transport_fails_the_send()
    {
        var (sender, outbox) = Build(Config(Transport(FromPolicyKind.IpUnrestricted)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Acme, Message() with { TransportName = "missing" }));

        Assert.Empty(outbox.Enqueued);
    }

    [Fact]
    public async Task No_relay_configured_enqueues_nothing()
    {
        var (sender, outbox) = Build(config: null);

        await sender.SendAsync(Space.Shared, Message());

        Assert.Empty(outbox.Enqueued);
    }

    private sealed class RecordingOutbox : IMessageOutbox
    {
        public List<(Space Space, EmailMessage Message, string? Transport)> Enqueued { get; } = [];

        public Task<Guid> EnqueueAsync(Space space, EmailMessage message, string? transportName, CancellationToken cancellationToken = default)
        {
            Enqueued.Add((space, message, transportName));
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
