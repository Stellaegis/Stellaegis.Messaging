using FluentAssertions;
using Stellaegis.Messaging.Outbox;
using Xunit;

namespace Stellaegis.Messaging.Tests;

public sealed class InMemoryMessageOutboxStoreTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static EmailMessage Message(string? idempotencyKey = null) => new()
    {
        To = [new EmailAddress("dest@example.test")],
        Subject = "Hello",
        HtmlBody = "<p>Hi</p>",
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task Enqueue_with_same_idempotency_key_is_neutralized()
    {
        var store = new InMemoryMessageOutboxStore();

        var first = await store.EnqueueAsync("acme", Message("k1"), "default");
        var second = await store.EnqueueAsync("acme", Message("k1"), "default");

        second.Should().Be(first);
        store.Snapshot().Should().ContainSingle();
    }

    [Fact]
    public async Task Enqueue_without_key_always_creates_a_new_entry()
    {
        var store = new InMemoryMessageOutboxStore();

        await store.EnqueueAsync("acme", Message(), "default");
        await store.EnqueueAsync("acme", Message(), "default");

        store.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task Same_key_across_tenants_are_independent()
    {
        var store = new InMemoryMessageOutboxStore();

        await store.EnqueueAsync("acme", Message("k1"), "default");
        await store.EnqueueAsync("globex", Message("k1"), "default");

        store.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task Claim_leases_entries_so_a_second_claim_skips_them()
    {
        var store = new InMemoryMessageOutboxStore();
        await store.EnqueueAsync("acme", Message(), "default");

        var first = await store.ClaimDueAsync(10, Lease);
        var second = await store.ClaimDueAsync(10, Lease);

        first.Should().ContainSingle();
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task Retry_reschedules_in_the_future_and_is_not_claimed_until_due()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync("acme", Message(), "default");
        var claimed = await store.ClaimDueAsync(10, Lease);

        await store.MarkForRetryAsync(id, DateTimeOffset.UtcNow.AddHours(1), "boom");

        var snap = store.Snapshot().Should().ContainSingle().Subject;
        snap.Status.Should().Be(OutboxStatus.Pending);
        snap.Attempts.Should().Be(1);
        snap.LastError.Should().Be("boom");
        (await store.ClaimDueAsync(10, Lease)).Should().BeEmpty();
        claimed.Should().ContainSingle();
    }

    [Fact]
    public async Task MarkSent_without_retention_removes_the_entry()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync("acme", Message(), "default");

        await store.MarkSentAsync(id, retainUntil: null);

        store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task MarkSent_with_retention_keeps_the_entry_as_sent()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync("acme", Message(), "default");

        await store.MarkSentAsync(id, DateTimeOffset.UtcNow.AddDays(1));

        var snap = store.Snapshot().Should().ContainSingle().Subject;
        snap.Status.Should().Be(OutboxStatus.Sent);
        (await store.ClaimDueAsync(10, Lease)).Should().BeEmpty();
    }

    [Fact]
    public async Task PurgeExpired_removes_terminal_entries_past_their_deadline_only()
    {
        var store = new InMemoryMessageOutboxStore();
        var expired = await store.EnqueueAsync("acme", Message(), "default");
        var kept = await store.EnqueueAsync("acme", Message(), "default");

        await store.MarkDeadLetterAsync(expired, DateTimeOffset.UtcNow.AddMinutes(-1), "dead");
        await store.MarkDeadLetterAsync(kept, DateTimeOffset.UtcNow.AddDays(30), "dead");

        var removed = await store.PurgeExpiredAsync();

        removed.Should().Be(1);
        store.Snapshot().Should().ContainSingle().Which.MessageId.Should().Be(kept);
    }
}
