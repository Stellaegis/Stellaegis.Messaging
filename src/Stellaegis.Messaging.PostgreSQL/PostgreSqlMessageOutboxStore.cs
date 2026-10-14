using System.Data.Common;
using System.Text.Json;
using Dapper;
using Stellaegis.Database;
using Stellaegis.Messaging.Outbox;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// PostgreSQL-backed <see cref="IMessageOutboxStore"/>. The message payload (body + recipients =
/// personal data) is encrypted at rest with pgcrypto (<c>pgp_sym_encrypt</c>); the passphrase comes
/// from configuration (<see cref="MessagingDatabaseOptions"/>), never from another store. Claiming uses
/// <c>FOR UPDATE SKIP LOCKED</c> with a lease (the claim pushes <c>next_attempt_at</c> forward) so the
/// send happens outside any held lock; at-least-once delivery, the idempotency key neutralizing a replay.
/// </summary>
/// <remarks>Scoped: owns its <see cref="DbConnection"/> and returns it to the pool on dispose.</remarks>
internal sealed class PostgreSqlMessageOutboxStore : IMessageOutboxStore, IDisposable
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly DbConnection _connection;
    private readonly MessagingDatabaseOptions _options;

    static PostgreSqlMessageOutboxStore()
    {
        SqlMapper.SetTypeMap(typeof(ClaimRow), new SnakeCaseTypeMap<ClaimRow>());
    }

    internal PostgreSqlMessageOutboxStore(DbConnection connection, MessagingDatabaseOptions options)
    {
        _connection = connection;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(
        string tenantSlug,
        EmailMessage message,
        string? transportName,
        CancellationToken cancellationToken = default)
    {
        var payloadJson = JsonSerializer.Serialize(message, PayloadJson);

        const string sql = """
            insert into msg.outbox (tenant_slug, channel, payload, key_slot, idempotency_key, transport_name)
            values (@tenantSlug, 'email'::msg.channel, pgp_sym_encrypt(@payloadJson, @passphrase), @keySlot,
                    @idempotencyKey, @transportName)
            on conflict (tenant_slug, idempotency_key) where idempotency_key is not null do nothing
            returning outbox_unique_id
            """;

        var parameters = new
        {
            tenantSlug,
            payloadJson,
            passphrase = _options.ActivePassphrase,
            keySlot = (short)_options.ActiveKeySlot,
            idempotencyKey = message.IdempotencyKey,
            transportName,
        };

        var inserted = await _connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (inserted is { } id)
            return id;

        // Conflict on (tenant_slug, idempotency_key): an entry already exists — return its id (neutralized).
        return await _connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "select outbox_unique_id from msg.outbox where tenant_slug = @tenantSlug and idempotency_key = @idempotencyKey",
            new { tenantSlug, idempotencyKey = message.IdempotencyKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEntry>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        // Claim due pending rows (FIFO), lease them by pushing next_attempt_at forward so a concurrent
        // claim skips them, and return the decrypted payload for delivery.
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
                o.outbox_unique_id,
                o.tenant_slug,
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

        var rows = await _connection.QueryAsync<ClaimRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(Guid messageId, DateTimeOffset? retainUntil, CancellationToken cancellationToken = default)
    {
        if (retainUntil is null)
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                "delete from msg.outbox where outbox_unique_id = @messageId",
                new { messageId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await _connection.ExecuteAsync(new CommandDefinition(
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
        await _connection.ExecuteAsync(new CommandDefinition(
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
        await _connection.ExecuteAsync(new CommandDefinition(
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
        return await _connection.ExecuteAsync(new CommandDefinition(
            "delete from msg.outbox where status <> 'pending'::msg.outbox_status and purge_at <= now()",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    private static OutboxEntry ToEntry(ClaimRow r) => new()
    {
        MessageId = r.OutboxUniqueId,
        TenantSlug = r.TenantSlug,
        Message = JsonSerializer.Deserialize<EmailMessage>(r.PayloadJson, PayloadJson)
            ?? throw new InvalidOperationException($"Outbox payload for {r.OutboxUniqueId} deserialized to null."),
        TransportName = r.TransportName,
        Attempts = r.Attempts,
        CreatedAt = r.CreatedAt,
    };

    private sealed class ClaimRow
    {
        public Guid OutboxUniqueId { get; set; }
        public string TenantSlug { get; set; } = "";
        public string? TransportName { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string PayloadJson { get; set; } = "";
    }
}
