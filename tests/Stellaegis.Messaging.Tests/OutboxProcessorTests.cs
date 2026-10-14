using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;
using static Stellaegis.Messaging.Tests.Spaces;

namespace Stellaegis.Messaging.Tests;

public sealed class OutboxProcessorTests
{
    private static EmailMessage Message() => new()
    {
        To = [new EmailAddress("dest@example.test")],
        Subject = "Hello",
        HtmlBody = "<p>Hi</p>",
    };

    // Builds the processor over an in-memory store with the given resolver config and fast options.
    private static Harness Build(MessagingConfig? config, Action<MessagingOutboxOptions>? configure = null)
    {
        var transport = new FakeEmailTransport();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailConfigResolver>(new FakeEmailConfigResolver(config));
        services.AddSingleton<IEmailTransport>(transport);
        services.AddSingleton<InMemoryMessageOutboxStore>();
        services.AddSingleton<IMessageOutboxStore>(sp => sp.GetRequiredService<InMemoryMessageOutboxStore>());

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<InMemoryMessageOutboxStore>();

        // Zero backoff base → a retried entry is immediately due again, so a follow-up cycle picks it up.
        var options = new MessagingOutboxOptions { RetryBackoffBase = TimeSpan.Zero, MaxAttempts = 3 };
        configure?.Invoke(options);

        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxProcessor>.Instance,
            Options.Create(options));

        return new Harness(processor, store, transport);
    }

    [Fact]
    public async Task Enqueued_message_is_delivered_verbatim_and_removed()
    {
        var h = Build(FakeEmailConfigResolver.WithDefaultTransport());
        await h.Store.EnqueueAsync(Acme, Message(), "default");

        var processed = await h.Processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var sent = Assert.Single(h.Transport.Sent);
        Assert.Equal("Hello", sent.Subject);
        Assert.Equal("dest@example.test", Assert.Single(sent.To).Address);
        Assert.Empty(h.Store.Snapshot());
    }

    [Fact]
    public async Task Transient_failure_is_retried_then_delivered()
    {
        var h = Build(FakeEmailConfigResolver.WithDefaultTransport());
        h.Transport.FailuresBeforeSuccess = 1;
        await h.Store.EnqueueAsync(Acme, Message(), "default");

        await h.Processor.ProcessOnceAsync(CancellationToken.None); // attempt 1 fails → retry
        var afterFirst = Assert.Single(h.Store.Snapshot());
        Assert.Equal(OutboxStatus.Pending, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);

        await h.Processor.ProcessOnceAsync(CancellationToken.None); // attempt 2 succeeds

        Assert.Single(h.Transport.Sent);
        Assert.Empty(h.Store.Snapshot());
    }

    [Fact]
    public async Task Permanent_failure_is_dead_lettered_after_the_attempt_cap()
    {
        var h = Build(FakeEmailConfigResolver.WithDefaultTransport(), o => o.MaxAttempts = 3);
        h.Transport.AlwaysFail = true;
        await h.Store.EnqueueAsync(Acme, Message(), "default");

        for (var i = 0; i < 3; i++)
            await h.Processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(3, h.Transport.Attempts);
        var snap = Assert.Single(h.Store.Snapshot());
        Assert.Equal(OutboxStatus.DeadLetter, snap.Status);
        Assert.Equal(3, snap.Attempts);
        Assert.False(string.IsNullOrEmpty(snap.LastError));
    }

    [Fact]
    public async Task No_relay_at_send_time_is_a_failure_and_never_reaches_the_transport()
    {
        var h = Build(config: null); // resolver returns null = no relay configured
        await h.Store.EnqueueAsync(Acme, Message(), "default");

        await h.Processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(0, h.Transport.Attempts);
        var snap = Assert.Single(h.Store.Snapshot());
        Assert.Equal(OutboxStatus.Pending, snap.Status);
        Assert.Equal(1, snap.Attempts);
        Assert.Contains("relay", snap.LastError);
    }

    private sealed record Harness(OutboxProcessor Processor, InMemoryMessageOutboxStore Store, FakeEmailTransport Transport);
}
