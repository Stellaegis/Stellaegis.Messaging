using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.Configuration;

/// <summary>
/// Tuning for the outbox worker (<c>OutboxProcessor</c>): polling cadence, claim batch, the retry
/// backoff schedule and the two distinct retention windows. Bound from <c>Stellaegis:Messaging:Outbox</c>, and
/// validated when the host starts: a cadence of zero or a batch of none fails the start, naming the setting.
/// </summary>
public sealed class MessagingOutboxOptions : IValidatableOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging:Outbox";

    /// <summary>How often the worker polls for due entries. Default 5 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum entries claimed and delivered per polling cycle. Default 20.</summary>
    public int ClaimBatchSize { get; set; } = 20;

    /// <summary>
    /// Lease held on a claimed entry while it is being delivered: it stays invisible to other claims for
    /// this long, then (on a worker crash) becomes due again for retry. Default 5 minutes.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Attempt cap before an entry is dead-lettered. Default 10 (≈ a 24 h retry window with the default backoff).</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Base delay of the exponential backoff (<c>base × 2^attempts</c>, clamped to <see cref="RetryBackoffCap"/>). Default 1 minute.</summary>
    public TimeSpan RetryBackoffBase { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Upper clamp of the exponential backoff. Default 1 hour.</summary>
    public TimeSpan RetryBackoffCap { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a successfully sent entry is retained ("Sent items"). Default <see cref="TimeSpan.Zero"/>
    /// = fire-and-forget (the entry is deleted on success).
    /// </summary>
    public TimeSpan SentRetention { get; set; } = TimeSpan.Zero;

    /// <summary>How long a dead-lettered entry is kept for inspection / manual resend. Default 30 days.</summary>
    public TimeSpan DeadLetterTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the worker purges expired terminal entries. Default 1 hour.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public string? FindError()
    {
        if (PollingInterval <= TimeSpan.Zero)
            return $"PollingInterval is {PollingInterval}: it must be positive.";
        if (ClaimBatchSize < 1)
            return $"ClaimBatchSize is {ClaimBatchSize}: it must be at least 1.";
        if (LeaseDuration <= TimeSpan.Zero)
            return $"LeaseDuration is {LeaseDuration}: it must be positive.";
        if (MaxAttempts < 1)
            return $"MaxAttempts is {MaxAttempts}: it must be at least 1.";
        if (RetryBackoffBase < TimeSpan.Zero)
            return $"RetryBackoffBase is {RetryBackoffBase}: it must be zero or positive.";
        if (RetryBackoffCap < RetryBackoffBase)
            return $"RetryBackoffCap is {RetryBackoffCap}, below RetryBackoffBase {RetryBackoffBase}.";
        if (SentRetention < TimeSpan.Zero)
            return $"SentRetention is {SentRetention}: it must be zero or positive.";
        if (DeadLetterTtl <= TimeSpan.Zero)
            return $"DeadLetterTtl is {DeadLetterTtl}: it must be positive.";
        if (PurgeInterval <= TimeSpan.Zero)
            return $"PurgeInterval is {PurgeInterval}: it must be positive.";
        return null;
    }
}
