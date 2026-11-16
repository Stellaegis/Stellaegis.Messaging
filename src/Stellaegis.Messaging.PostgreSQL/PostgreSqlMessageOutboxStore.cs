using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;

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
/// connection of its own, so producers and the worker never share one. It goes through no unit of work of
/// <c>Stellaegis.Primitives.Data</c> and sets no space on its session: the outbox is a buffer of the sending
/// engine, whose worker claims the messages of every compartment at once.
/// </remarks>
internal sealed class PostgreSqlMessageOutboxStore : IMessageOutboxStore, IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly MessagingDatabaseOptions _options;
    private readonly ILogger<PostgreSqlMessageOutboxStore> _logger;


    /// <summary>Creates the store, validating its options.</summary>
    /// <exception cref="OptionsValidationException">The options keep the store from working.</exception>
    public PostgreSqlMessageOutboxStore(IOptions<MessagingDatabaseOptions> options, ILogger<PostgreSqlMessageOutboxStore> logger)
    {
        _options = Guard.NotNull(options).Value;
        _logger = Guard.NotNull(logger);

        // Registered through AddStellaegisEmailPostgreSql, the options are validated before this point — at host
        // start, or on first resolution without a host. Built by hand, they are validated here.
        if (_options.FindError() is { } error)
            throw new OptionsValidationException(Options.DefaultName, typeof(MessagingDatabaseOptions), [error]);

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

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = new NpgsqlCommand(sql, connection);
        insert.Parameters.Add("compartment", NpgsqlDbType.Text).Value = (object?)space.Compartment?.Value ?? DBNull.Value;
        insert.Parameters.Add("payloadJson", NpgsqlDbType.Text).Value = payloadJson;
        insert.Parameters.Add("passphrase", NpgsqlDbType.Text).Value = _options.ActivePassphrase;
        insert.Parameters.Add("keySlot", NpgsqlDbType.Smallint).Value = (short)_options.ActiveKeySlot;
        insert.Parameters.Add("idempotencyKey", NpgsqlDbType.Text).Value = (object?)message.IdempotencyKey ?? DBNull.Value;
        insert.Parameters.Add("transportName", NpgsqlDbType.Text).Value = (object?)transportName ?? DBNull.Value;

        // No row returned: the conflict on (compartment, idempotency_key) left the insert undone.
        if (await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is Guid id)
            return id;

        // An entry already exists — return its id (neutralized).
        await using var existing = new NpgsqlCommand(
            """
            select outbox_unique_id
            from   msg.outbox
            where  compartment is not distinct from @compartment
              and  idempotency_key = @idempotencyKey
            """,
            connection);
        existing.Parameters.Add("compartment", NpgsqlDbType.Text).Value = (object?)space.Compartment?.Value ?? DBNull.Value;
        existing.Parameters.Add("idempotencyKey", NpgsqlDbType.Text).Value = (object?)message.IdempotencyKey ?? DBNull.Value;
        return (Guid)(await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
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

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("batchSize", NpgsqlDbType.Integer).Value = batchSize;
        command.Parameters.Add("lease", NpgsqlDbType.Interval).Value = leaseDuration;
        command.Parameters.Add("pass1", NpgsqlDbType.Text).Value = _options.GetPassphrase(1);
        command.Parameters.Add("pass2", NpgsqlDbType.Text).Value = _options.GetPassphrase(2);

        var claimed = new List<(long OutboxId, OutboxEntry Entry)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                claimed.Add((reader.GetInt64("outbox_id"), Read(reader)));
        }

        return claimed.OrderBy(row => row.OutboxId).Select(row => row.Entry).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(Guid messageId, DateTimeOffset? retainUntil, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (retainUntil is null)
        {
            await using var delete = new NpgsqlCommand("delete from msg.outbox where outbox_unique_id = @messageId", connection);
            delete.Parameters.Add("messageId", NpgsqlDbType.Uuid).Value = messageId;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var command = new NpgsqlCommand(
            """
            update msg.outbox
            set    status = 'sent'::msg.outbox_status, sent_at = now(), purge_at = @retainUntil
            where  outbox_unique_id = @messageId
            """,
            connection);
        command.Parameters.Add("messageId", NpgsqlDbType.Uuid).Value = messageId;
        command.Parameters.Add("retainUntil", NpgsqlDbType.TimestampTz).Value = retainUntil.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkForRetryAsync(Guid messageId, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            update msg.outbox
            set    attempts = attempts + 1, next_attempt_at = @nextAttemptAt, last_error = @error
            where  outbox_unique_id = @messageId
            """,
            connection);
        command.Parameters.Add("messageId", NpgsqlDbType.Uuid).Value = messageId;
        command.Parameters.Add("nextAttemptAt", NpgsqlDbType.TimestampTz).Value = nextAttemptAt;
        command.Parameters.Add("error", NpgsqlDbType.Text).Value = error;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkDeadLetterAsync(Guid messageId, DateTimeOffset purgeAt, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            update msg.outbox
            set    status = 'dead_letter'::msg.outbox_status, attempts = attempts + 1,
                   failed_at = now(), purge_at = @purgeAt, last_error = @error
            where  outbox_unique_id = @messageId
            """,
            connection);
        command.Parameters.Add("messageId", NpgsqlDbType.Uuid).Value = messageId;
        command.Parameters.Add("purgeAt", NpgsqlDbType.TimestampTz).Value = purgeAt;
        command.Parameters.Add("error", NpgsqlDbType.Text).Value = error;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            "delete from msg.outbox where status <> 'pending'::msg.outbox_status and purge_at <= now()",
            connection);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PurgeCompartmentAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        _logger.LogInformation("Purging the outbox of compartment {Compartment} by {Actor}.", compartment, actor);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand("delete from msg.outbox where compartment = @compartment", connection);
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment.Value;
        var removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Purged {Count} messages of compartment {Compartment} by {Actor}.", removed, compartment, actor);
        return removed;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _dataSource.Dispose();

    private static OutboxEntry Read(NpgsqlDataReader row)
    {
        var messageId = row.GetGuid("outbox_unique_id");
        return new()
        {
            MessageId = messageId,
            Space = row.IsDBNull("compartment") ? Space.Shared : Space.Of(Compartment.Parse(row.GetString("compartment"))),
            Message = JsonSerializer.Deserialize<EmailMessage>(row.GetString("payload_json"), PayloadJson)
                ?? throw new InvalidOperationException($"Outbox payload for {messageId} deserialized to null."),
            TransportName = row.IsDBNull("transport_name") ? null : row.GetString("transport_name"),
            Attempts = row.GetInt32("attempts"),
            CreatedAt = row.GetFieldValue<DateTimeOffset>("created_at"),
        };
    }
}
