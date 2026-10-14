namespace Stellaegis.Messaging.Outbox;

/// <summary>
/// In-memory <see cref="IMessageOutboxStore"/> for F5 / InMemory hosts: it exercises the exact same
/// enqueue → claim → deliver → retry / dead-letter path as the PostgreSQL store, without persistence.
/// Not durable across a restart — hosts that need durability register the PostgreSQL store instead.
/// </summary>
/// <remarks>
/// Thread-safe: producers enqueue while the worker claims. Idempotency is existence-based (mirrors the
/// PostgreSQL <c>ux_outbox_idempotency</c> unique index over any status): while an entry with a given
/// (tenant, key) exists, a re-enqueue returns the existing id. FIFO ordering uses a monotonic sequence.
/// </remarks>
internal sealed class InMemoryMessageOutboxStore : IMessageOutboxStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private long _sequence;

    /// <inheritdoc />
    public Task<Guid> EnqueueAsync(
        string tenantSlug,
        EmailMessage message,
        string? transportName,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var key = message.IdempotencyKey;
            if (key is not null)
            {
                var existing = _entries.Values.FirstOrDefault(
                    e => e.TenantSlug == tenantSlug && e.IdempotencyKey == key);
                if (existing is not null)
                    return Task.FromResult(existing.MessageId);
            }

            var id = Guid.NewGuid();
            _entries[id] = new Entry
            {
                MessageId = id,
                TenantSlug = tenantSlug,
                Message = message,
                TransportName = transportName,
                IdempotencyKey = key,
                Sequence = Interlocked.Increment(ref _sequence),
                Status = OutboxStatus.Pending,
                Attempts = 0,
                CreatedAt = now,
                NextAttemptAt = now,
                LeasedUntil = DateTimeOffset.MinValue,
            };
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEntry>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var due = _entries.Values
                .Where(e => e.Status == OutboxStatus.Pending && e.NextAttemptAt <= now && e.LeasedUntil <= now)
                .OrderBy(e => e.Sequence)
                .Take(batchSize)
                .ToList();

            foreach (var e in due)
                e.LeasedUntil = now + leaseDuration;

            IReadOnlyList<OutboxEntry> claimed = due.Select(ToClaim).ToList();
            return Task.FromResult(claimed);
        }
    }

    /// <inheritdoc />
    public Task MarkSentAsync(Guid messageId, DateTimeOffset? retainUntil, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(messageId, out var e))
                return Task.CompletedTask;

            if (retainUntil is null)
            {
                _entries.Remove(messageId);
            }
            else
            {
                e.Status = OutboxStatus.Sent;
                e.PurgeAt = retainUntil;
            }
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task MarkForRetryAsync(Guid messageId, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(messageId, out var e))
            {
                e.Attempts++;
                e.NextAttemptAt = nextAttemptAt;
                e.LeasedUntil = DateTimeOffset.MinValue; // release the lease
                e.LastError = error;
            }
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task MarkDeadLetterAsync(Guid messageId, DateTimeOffset purgeAt, string error, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(messageId, out var e))
            {
                e.Attempts++;
                e.Status = OutboxStatus.DeadLetter;
                e.PurgeAt = purgeAt;
                e.LastError = error;
            }
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            var expired = _entries.Values
                .Where(e => e.Status != OutboxStatus.Pending && e.PurgeAt is { } p && p <= now)
                .Select(e => e.MessageId)
                .ToList();

            foreach (var id in expired)
                _entries.Remove(id);

            return Task.FromResult(expired.Count);
        }
    }

    /// <summary>Test-only snapshot of the current entries (see <c>Stellaegis.Messaging.Tests</c>).</summary>
    internal IReadOnlyList<OutboxSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Values
                .OrderBy(e => e.Sequence)
                .Select(e => new OutboxSnapshot(e.MessageId, e.TenantSlug, e.Status, e.Attempts, e.LastError))
                .ToList();
        }
    }

    private static OutboxEntry ToClaim(Entry e) => new()
    {
        MessageId = e.MessageId,
        TenantSlug = e.TenantSlug,
        Message = e.Message,
        TransportName = e.TransportName,
        Attempts = e.Attempts,
        CreatedAt = e.CreatedAt,
    };

    private sealed class Entry
    {
        public required Guid MessageId { get; init; }
        public required string TenantSlug { get; init; }
        public required EmailMessage Message { get; init; }
        public string? TransportName { get; init; }
        public string? IdempotencyKey { get; init; }
        public required long Sequence { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public OutboxStatus Status { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public DateTimeOffset LeasedUntil { get; set; }
        public DateTimeOffset? PurgeAt { get; set; }
        public string? LastError { get; set; }
    }
}

/// <summary>Lifecycle status of an outbox entry (mirrors <c>msg.outbox_status</c>).</summary>
internal enum OutboxStatus
{
    Pending,
    Sent,
    DeadLetter,
}

/// <summary>Test-only projection of an in-memory outbox entry.</summary>
internal sealed record OutboxSnapshot(Guid MessageId, string TenantSlug, OutboxStatus Status, int Attempts, string? LastError);
