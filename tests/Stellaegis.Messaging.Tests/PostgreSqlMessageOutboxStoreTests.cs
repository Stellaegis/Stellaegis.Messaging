using Microsoft.Extensions.Options;
using Npgsql;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Messaging.PostgreSQL;
using Stellaegis.Primitives;
using static Stellaegis.Messaging.Tests.Spaces;

namespace Stellaegis.Messaging.Tests;

// The tests of this class share one database and run one after the other; each starts from an empty outbox.
public sealed class PostgreSqlMessageOutboxStoreTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(database.Unavailable is not null, database.Unavailable ?? string.Empty);
        await ExecuteAsync("delete from msg.outbox");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private PostgreSqlMessageOutboxStore Store(Action<MessagingDatabaseOptions>? configure = null)
    {
        var options = new MessagingDatabaseOptions { ConnectionString = database.ConnectionString, Passphrase = "slot-one" };
        configure?.Invoke(options);
        return new PostgreSqlMessageOutboxStore(Options.Create(options));
    }

    private static EmailMessage Message(string subject = "Hello", string? idempotencyKey = null) => new()
    {
        From = new EmailAddress("noreply@example.test"),
        To = [new EmailAddress("dest@example.test", "Dest")],
        Subject = subject,
        HtmlBody = "<p>Hi</p>",
        IdempotencyKey = idempotencyKey,
    };

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Enqueued_message_is_claimed_whole_with_its_compartment_and_transport()
    {
        await using var store = Store();
        var id = await store.EnqueueAsync(Acme, Message(), "billing");

        var entry = Assert.Single(await store.ClaimDueAsync(10, Lease));

        Assert.Equal(id, entry.MessageId);
        Assert.Equal(Acme, entry.Space);
        Assert.Equal("billing", entry.TransportName);
        Assert.Equal(0, entry.Attempts);
        Assert.Equal("Hello", entry.Message.Subject);
        Assert.Equal(new EmailAddress("dest@example.test", "Dest"), Assert.Single(entry.Message.To));
    }

    [Fact]
    public async Task Shared_space_is_stored_as_no_compartment_and_read_back_as_shared()
    {
        await using var store = Store();
        await store.EnqueueAsync(Space.Shared, Message(), null);

        Assert.Equal(1L, await ScalarAsync("select count(*) from msg.outbox where compartment is null"));
        Assert.Equal(Space.Shared, Assert.Single(await store.ClaimDueAsync(10, Lease)).Space);
    }

    [Fact]
    public async Task Payload_is_encrypted_at_rest()
    {
        await using var store = Store();
        await store.EnqueueAsync(Acme, Message("A readable subject"), null);

        Assert.Equal(0, await ScalarAsync(
            "select position(convert_to('A readable subject', 'UTF8') in payload) from msg.outbox"));
        Assert.Equal((short)1, await ScalarAsync("select key_slot from msg.outbox"));
    }

    [Fact]
    public async Task Rows_of_both_slots_stay_readable_through_a_rotation()
    {
        await using (var before = Store())
            await before.EnqueueAsync(Acme, Message("before"), null);

        await using var after = Store(o =>
        {
            o.Passphrase = string.Empty;
            o.Keys = new() { [1] = "slot-one", [2] = "slot-two" };
            o.ActiveKeySlot = 2;
        });
        await after.EnqueueAsync(Acme, Message("after"), null);

        var claimed = await after.ClaimDueAsync(10, Lease);

        Assert.Equal(["before", "after"], claimed.Select(e => e.Message.Subject));
        Assert.Equal(2L, await ScalarAsync("select count(distinct key_slot) from msg.outbox"));
    }

    [Fact]
    public async Task Same_idempotency_key_is_neutralized_in_a_compartment_and_in_the_shared_space()
    {
        await using var store = Store();

        var first = await store.EnqueueAsync(Acme, Message(idempotencyKey: "k1"), null);
        var second = await store.EnqueueAsync(Acme, Message(idempotencyKey: "k1"), null);
        var sharedFirst = await store.EnqueueAsync(Space.Shared, Message(idempotencyKey: "k1"), null);
        var sharedSecond = await store.EnqueueAsync(Space.Shared, Message(idempotencyKey: "k1"), null);
        await store.EnqueueAsync(Globex, Message(idempotencyKey: "k1"), null);

        Assert.Equal(first, second);
        Assert.Equal(sharedFirst, sharedSecond);
        Assert.NotEqual(first, sharedFirst);
        Assert.Equal(3L, await ScalarAsync("select count(*) from msg.outbox"));
    }

    [Fact]
    public async Task Claim_is_fifo_and_leases_what_it_returns()
    {
        await using var store = Store();
        for (var i = 0; i < 5; i++)
            await store.EnqueueAsync(Acme, Message($"m{i}"), null);

        var first = await store.ClaimDueAsync(3, Lease);
        var second = await store.ClaimDueAsync(10, Lease);
        var third = await store.ClaimDueAsync(10, Lease);

        Assert.Equal(["m0", "m1", "m2"], first.Select(e => e.Message.Subject));
        Assert.Equal(["m3", "m4"], second.Select(e => e.Message.Subject));
        Assert.Empty(third);
    }

    [Fact]
    public async Task Retry_counts_the_attempt_and_waits_until_due()
    {
        await using var store = Store();
        var id = await store.EnqueueAsync(Acme, Message(), null);
        await store.ClaimDueAsync(10, Lease);

        await store.MarkForRetryAsync(id, DateTimeOffset.UtcNow.AddHours(1), "boom");

        Assert.Empty(await store.ClaimDueAsync(10, Lease));
        Assert.Equal(1, await ScalarAsync("select attempts from msg.outbox"));
        Assert.Equal("boom", await ScalarAsync("select last_error from msg.outbox"));

        await ExecuteAsync("update msg.outbox set next_attempt_at = now()");
        Assert.Equal(1, Assert.Single(await store.ClaimDueAsync(10, Lease)).Attempts);
    }

    [Fact]
    public async Task Sent_without_retention_is_deleted_and_with_retention_is_kept_until_its_deadline()
    {
        await using var store = Store();
        var dropped = await store.EnqueueAsync(Acme, Message(), null);
        var kept = await store.EnqueueAsync(Acme, Message(), null);

        await store.MarkSentAsync(dropped, retainUntil: null);
        await store.MarkSentAsync(kept, DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1L, await ScalarAsync("select count(*) from msg.outbox"));
        Assert.Equal("sent", await ScalarAsync("select status::text from msg.outbox"));
        Assert.Empty(await store.ClaimDueAsync(10, Lease));
        Assert.Equal(0, await store.PurgeExpiredAsync());
    }

    [Fact]
    public async Task Purge_removes_terminal_rows_past_their_deadline_only()
    {
        await using var store = Store();
        var expired = await store.EnqueueAsync(Acme, Message(), null);
        var kept = await store.EnqueueAsync(Acme, Message(), null);
        await store.EnqueueAsync(Acme, Message(), null);

        await store.MarkDeadLetterAsync(expired, DateTimeOffset.UtcNow.AddMinutes(-1), "dead");
        await store.MarkDeadLetterAsync(kept, DateTimeOffset.UtcNow.AddDays(30), "dead");

        Assert.Equal(1, await store.PurgeExpiredAsync());
        Assert.Equal(2L, await ScalarAsync("select count(*) from msg.outbox"));
        Assert.Equal(1L, await ScalarAsync("select count(*) from msg.outbox where purge_at is null and status = 'pending'"));
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("ab")]
    [InlineData("acme-")]
    [InlineData("ac--me")]
    [InlineData("1acme")]
    public async Task Compartment_column_refuses_what_a_compartment_cannot_be(string value)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"insert into msg.outbox (compartment, channel, payload) values ('{value}', 'email', ''::bytea)"));

        Assert.Equal("ck_outbox_compartment", error.ConstraintName);
    }

    [Fact]
    public async Task Pending_row_with_a_purge_deadline_is_refused()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "insert into msg.outbox (compartment, channel, payload, purge_at) values ('acme', 'email', ''::bytea, now())"));

        Assert.Equal("ck_outbox_status_dates", error.ConstraintName);
    }

    [Fact]
    public void Store_refuses_to_start_without_a_passphrase_for_the_active_slot()
    {
        Assert.Throws<InvalidOperationException>(() => Store(o => o.Passphrase = string.Empty));
        Assert.Throws<InvalidOperationException>(() => Store(o => o.ActiveKeySlot = 2));
    }
}
