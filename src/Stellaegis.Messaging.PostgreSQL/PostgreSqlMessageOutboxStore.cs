using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// PostgreSQL-backed <see cref="IMessageOutboxStore"/>. The message payload (body + recipients =
/// personal data) is encrypted at rest with pgcrypto (<c>pgp_sym_encrypt</c>); the passphrase comes
/// from configuration (<see cref="MessagingDatabaseOptions"/>), never from another store. Claiming uses
/// <c>for update skip locked</c> with a lease (the claim pushes <c>next_attempt_at</c> forward) so the
/// send happens outside any held lock; at-least-once delivery, the idempotency key neutralizing a replay.
/// </summary>
/// <remarks>
/// A singleton: it owns its <see cref="NpgsqlDataSource"/>, and each operation opens a pooled
/// connection of its own, so producers and the worker never share one.
/// </remarks>
internal sealed class PostgreSqlMessageOutboxStore : IMessageOutboxStore, IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly MessagingDatabaseOptions _options;

    static PostgreSqlMessageOutboxStore()
    {
        PrimitiveTypeHandlers.Register();
        SnakeCaseTypeMap<ClaimRow>.Register();
    }

    public PostgreSqlMessageOutboxStore(IOptions<MessagingDatabaseOptions> options)
    {
        _options = Guard.NotNull(options).Value;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException(
                $"Messaging outbox: no connection string configured ({MessagingDatabaseOptions.SectionName}:ConnectionString).");

        // Fails at construction when the active slot has no passphrase, rather than at the first enqueue.
        _ = _options.ActivePassphrase;

        _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(
        Space space,
        EmailMessage message,
        string? transportName,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(space);
        Guard.NotNull(message);

        var payloadJson = JsonSerializer.Serialize(message, PayloadJson);

        const string sql = """
            insert into msg.outbox (compartment, channel, payload, key_slot, idempotency_key, transport_name)
            values (@compartment, 'email'::msg.channel, pgp_sym_encrypt(@payloadJson, @passphrase), @keySlot,
                    @idempotencyKey, @transportName)
            on conflict (compartment, idempotency_key) where idempotency_key is not null do nothing
            returning outbox_unique_id
            """;

        var parameters = new
        {
            compartment = space.Compartment,
            payloadJson,
            passphrase = _options.ActivePassphrase,
            keySlot = (short)_options.ActiveKeySlot,
            idempotencyKey = message.IdempotencyKey,
            transportName,
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var inserted = await connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (inserted is { } id)
            return id;

        // Conflict on (compartment, idempotency_key): an entry already exists — return its id (neutralized).
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            select outbox_unique_id
            from   msg.outbox
            where  compartment is not distinct from @compartment
              and  idempotency_key = @idempotencyKey
            """,
            new { compartment = space.Compartment, idempotencyKey = message.IdempotencyKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEntry>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        // Claim due pending rows (FIFO), lease them by pushing next_attempt_at forward so a concurrent
        // claim skips them, and return the decrypted payload for delivery. "returning" keeps no order,
        // so the rows are put back in FIFO order here.
        const string sql = """
            with due as (
                select outbox_id
                from   msg.outbox
                where  status = 'pending'::msg.outbox_status
                  and  next_attempt_at <= now()
                order  by outbox_id
                limit  @batchSize
                for update skip locked
            )
            update msg.outbox o
            set    next_attempt_at = now() + @lease
            from   due
            where  o.outbox_id = due.outbox_id
            returning
                o.outbox_id,
                o.outbox_unique_id,
                o.compartment,
                o.transport_name,
                o.attempts,
                o.created_at,
                pgp_sym_decrypt(o.payload, case o.key_slot when 1 then @pass1 when 2 then @pass2 end)::text as payload_json
            """;

        var parameters = new
        {
            batchSize,
            lease = leaseDuration,
            pass1 = _options.GetPassphrase(1),
            pass2 = _options.GetPassphrase(2),
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ClaimRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.OrderBy(r => r.OutboxId).Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(Guid messageId, DateTimeOffset? retainUntil, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (retainUntil is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "delete from msg.outbox where outbox_unique_id = @messageId",
                new { messageId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update msg.outbox
            set    status = 'sent'::msg.outbox_status, sent_at = now(), purge_at = @retainUntil
            where  outbox_unique_id = @messageId
            """,
            new { messageId, retainUntil }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkForRetryAsync(Guid messageId, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update msg.outbox
            set    attempts = attempts + 1, next_attempt_at = @nextAttemptAt, last_error = @error
            where  outbox_unique_id = @messageId
            """,
            new { messageId, nextAttemptAt, error }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkDeadLetterAsync(Guid messageId, DateTimeOffset purgeAt, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update msg.outbox
            set    status = 'dead_letter'::msg.outbox_status, attempts = attempts + 1,
                   failed_at = now(), purge_at = @purgeAt, last_error = @error
            where  outbox_unique_id = @messageId
            """,
            new { messageId, purgeAt, error }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteAsync(new CommandDefinition(
            "delete from msg.outbox where status <> 'pending'::msg.outbox_status and purge_at <= now()",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _dataSource.Dispose();

    private static OutboxEntry ToEntry(ClaimRow r) => new()
    {
        MessageId = r.OutboxUniqueId,
        Space = r.Compartment is { } compartment ? Space.Of(compartment) : Space.Shared,
        Message = JsonSerializer.Deserialize<EmailMessage>(r.PayloadJson, PayloadJson)
            ?? throw new InvalidOperationException($"Outbox payload for {r.OutboxUniqueId} deserialized to null."),
        TransportName = r.TransportName,
        Attempts = r.Attempts,
        CreatedAt = r.CreatedAt,
    };

    private sealed class ClaimRow
    {
        public long OutboxId { get; set; }
        public Guid OutboxUniqueId { get; set; }
        public Compartment? Compartment { get; set; }
        public string? TransportName { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
    }
}
