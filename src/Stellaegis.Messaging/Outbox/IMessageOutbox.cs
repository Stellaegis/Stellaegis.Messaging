namespace Stellaegis.Messaging.Outbox;

/// <summary>
/// Producer-facing surface of the outbox: enqueues a finalized message for reliable delivery.
/// The orchestrating <see cref="IEmailSender"/> hands the frozen message here instead of sending it
/// inline; a background <see cref="OutboxProcessor"/> then claims, delivers, retries and dead-letters it.
/// </summary>
/// <remarks>
/// Enqueueing completes as soon as the message is durably buffered — not when it is delivered. This is
/// the intended fire-and-forget contract: the transport (relay connection, credentials) is late-bound by
/// the worker at send time, so a credential rotation between enqueue and delivery is picked up, and no
/// secret ever enters the buffer.
/// </remarks>
public interface IMessageOutbox
{
    /// <summary>
    /// Enqueues a finalized <paramref name="message"/> for the given tenant scope. The message must
    /// already be frozen (from-policy applied) — the worker sends it verbatim. When
    /// <see cref="EmailMessage.IdempotencyKey"/> is set and a non-terminal entry already exists for the
    /// same tenant and key, the existing message id is returned and nothing new is enqueued.
    /// </summary>
    /// <param name="tenantSlug">Tenant scope (or <c>"default"</c> for system mail), used to resolve the transport at send time.</param>
    /// <param name="message">The frozen message to deliver.</param>
    /// <param name="transportName">The transport name resolved at enqueue time, re-resolved against the vault at send time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stable public message id (<c>outbox_unique_id</c>).</returns>
    Task<Guid> EnqueueAsync(
        string tenantSlug,
        EmailMessage message,
        string? transportName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Storage surface of the outbox, driven by the <see cref="OutboxProcessor"/> worker and implemented
/// per backend (in-memory for F5, PostgreSQL for durable hosts). Extends <see cref="IMessageOutbox"/>
/// so a single registered instance serves both producers and the worker.
/// </summary>
/// <remarks>
/// The store owns persistence primitives only; the delivery <i>policy</i> (backoff schedule, attempt
/// cap, retention) lives in the worker. Claiming uses a lease rather than a lock held across the network
/// send: a claimed entry is made invisible for <c>leaseDuration</c>; if the worker crashes mid-send the
/// lease expires and the entry is retried (at-least-once, the idempotency key neutralizing a duplicate).
/// </remarks>
public interface IMessageOutboxStore : IMessageOutbox
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due pending entries (FIFO), leasing them for
    /// <paramref name="leaseDuration"/> so a concurrent claim skips them, and returns them for delivery.
    /// </summary>
    Task<IReadOnlyList<OutboxEntry>> ClaimDueAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an entry as successfully sent. When <paramref name="retainUntil"/> is <see langword="null"/>
    /// the entry is deleted (fire-and-forget default); otherwise it is kept as <c>sent</c> until that
    /// purge deadline ("Sent items" retention).
    /// </summary>
    Task MarkSentAsync(Guid messageId, DateTimeOffset? retainUntil, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a transient failure: the entry stays pending, its attempt count is incremented, its next
    /// attempt is scheduled at <paramref name="nextAttemptAt"/> (backoff) and <paramref name="error"/> is stored.
    /// </summary>
    Task MarkForRetryAsync(Guid messageId, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an entry to <c>dead_letter</c> (retries exhausted): a definitive failure kept for inspection
    /// / manual resend until <paramref name="purgeAt"/>, with <paramref name="error"/> stored.
    /// </summary>
    Task MarkDeadLetterAsync(Guid messageId, DateTimeOffset purgeAt, string error, CancellationToken cancellationToken = default);

    /// <summary>Deletes terminal entries (sent-retained or dead-letter) whose purge deadline has passed. Returns the number removed.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A claimed, due entry handed to the worker for one delivery attempt. The message is frozen and ready
/// to send; the transport connection is re-resolved from the vault by the worker.
/// </summary>
public sealed record OutboxEntry
{
    /// <summary>Stable public message id (<c>outbox_unique_id</c>).</summary>
    public required Guid MessageId { get; init; }

    /// <summary>Tenant scope used to resolve the transport (or <c>"default"</c> for system mail).</summary>
    public required string TenantSlug { get; init; }

    /// <summary>The frozen message to deliver.</summary>
    public required EmailMessage Message { get; init; }

    /// <summary>Transport name resolved at enqueue time; the worker re-resolves it against the vault.</summary>
    public string? TransportName { get; init; }

    /// <summary>Number of attempts already performed (drives the backoff and the dead-letter decision).</summary>
    public required int Attempts { get; init; }

    /// <summary>When the entry was first enqueued.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
