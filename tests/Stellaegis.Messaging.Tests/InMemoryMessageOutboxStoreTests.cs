using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;
using static Stellaegis.Messaging.Tests.Spaces;

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

        var first = await store.EnqueueAsync(Acme, Message("k1"), "default");
        var second = await store.EnqueueAsync(Acme, Message("k1"), "default");

        Assert.Equal(first, second);
        Assert.Single(store.Snapshot());
    }

    [Fact]
    public async Task Enqueue_without_key_always_creates_a_new_entry()
    {
        var store = new InMemoryMessageOutboxStore();

        await store.EnqueueAsync(Acme, Message(), "default");
        await store.EnqueueAsync(Acme, Message(), "default");

        Assert.Equal(2, store.Snapshot().Count);
    }

    [Fact]
    public async Task Same_key_across_spaces_are_independent()
    {
        var store = new InMemoryMessageOutboxStore();

        await store.EnqueueAsync(Acme, Message("k1"), "default");
        await store.EnqueueAsync(Globex, Message("k1"), "default");
        await store.EnqueueAsync(Space.Shared, Message("k1"), "default");

        Assert.Equal(3, store.Snapshot().Count);
    }

    [Fact]
    public async Task Same_key_in_the_shared_space_is_neutralized()
    {
        var store = new InMemoryMessageOutboxStore();

        var first = await store.EnqueueAsync(Space.Shared, Message("k1"), "default");
        var second = await store.EnqueueAsync(Space.Shared, Message("k1"), "default");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Claim_leases_entries_so_a_second_claim_skips_them()
    {
        var store = new InMemoryMessageOutboxStore();
        await store.EnqueueAsync(Acme, Message(), "default");

        var first = await store.ClaimDueAsync(10, Lease);
        var second = await store.ClaimDueAsync(10, Lease);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task Retry_reschedules_in_the_future_and_is_not_claimed_until_due()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync(Acme, Message(), "default");
        var claimed = await store.ClaimDueAsync(10, Lease);

        await store.MarkForRetryAsync(id, DateTimeOffset.UtcNow.AddHours(1), "boom");

        var snap = Assert.Single(store.Snapshot());
        Assert.Equal(OutboxStatus.Pending, snap.Status);
        Assert.Equal(1, snap.Attempts);
        Assert.Equal("boom", snap.LastError);
        Assert.Empty(await store.ClaimDueAsync(10, Lease));
        Assert.Single(claimed);
    }

    [Fact]
    public async Task MarkSent_without_retention_removes_the_entry()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync(Acme, Message(), "default");

        await store.MarkSentAsync(id, retainUntil: null);

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task MarkSent_with_retention_keeps_the_entry_as_sent()
    {
        var store = new InMemoryMessageOutboxStore();
        var id = await store.EnqueueAsync(Acme, Message(), "default");

        await store.MarkSentAsync(id, DateTimeOffset.UtcNow.AddDays(1));

        var snap = Assert.Single(store.Snapshot());
        Assert.Equal(OutboxStatus.Sent, snap.Status);
        Assert.Empty(await store.ClaimDueAsync(10, Lease));
    }

    [Fact]
    public async Task Purging_a_compartment_removes_its_entries_whatever_their_status_and_nothing_else()
    {
        var store = new InMemoryMessageOutboxStore();
        var pending = await store.EnqueueAsync(Acme, Message(), "default");
        var dead = await store.EnqueueAsync(Acme, Message(), "default");
        await store.MarkDeadLetterAsync(dead, DateTimeOffset.UtcNow.AddDays(30), "dead");
        await store.EnqueueAsync(Globex, Message(), "default");
        await store.EnqueueAsync(Space.Shared, Message(), "default");

        var removed = await store.PurgeCompartmentAsync(Acme.Compartment!, Actor.Job("purge"));
        var again = await store.PurgeCompartmentAsync(Acme.Compartment!, Actor.Job("purge"));

        Assert.Equal(2, removed);
        Assert.Equal(0, again);
        Assert.DoesNotContain(store.Snapshot(), e => e.MessageId == pending || e.MessageId == dead);
        Assert.Equal([Globex, Space.Shared], store.Snapshot().Select(e => e.Space));
    }

    [Fact]
    public async Task PurgeExpired_removes_terminal_entries_past_their_deadline_only()
    {
        var store = new InMemoryMessageOutboxStore();
        var expired = await store.EnqueueAsync(Acme, Message(), "default");
        var kept = await store.EnqueueAsync(Acme, Message(), "default");

        await store.MarkDeadLetterAsync(expired, DateTimeOffset.UtcNow.AddMinutes(-1), "dead");
        await store.MarkDeadLetterAsync(kept, DateTimeOffset.UtcNow.AddDays(30), "dead");

        var removed = await store.PurgeExpiredAsync();

        Assert.Equal(1, removed);
        Assert.Equal(kept, Assert.Single(store.Snapshot()).MessageId);
    }
}
