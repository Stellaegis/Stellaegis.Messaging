using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging.Outbox;

/// <summary>
/// Background worker that drains the outbox: it claims due entries, re-resolves the transport against
/// the vault, delivers via the registered <see cref="IEmailTransport"/>, and applies the retention /
/// retry / dead-letter policy. Also purges expired terminal entries on an interval.
/// </summary>
/// <remarks>
/// Each cycle runs in its own DI scope so a scoped store (PostgreSQL, one connection) claims and
/// finalizes on the same instance; entries are delivered sequentially within the cycle (a scoped
/// connection forbids concurrent commands). The message is sent verbatim (frozen at enqueue); only the
/// transport connection is late-bound here.
/// </remarks>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger,
    IOptions<MessagingOutboxOptions> options) : BackgroundService
{
    private const int MaxErrorLength = 4096; // mirrors msg.outbox.ck_outbox_last_error

    private readonly MessagingOutboxOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "OutboxProcessor started. Polling every {Interval}s.", _options.PollingInterval.TotalSeconds);

        var nextPurge = DateTimeOffset.UtcNow + _options.PurgeInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);

                if (DateTimeOffset.UtcNow >= nextPurge)
                {
                    await PurgeOnceAsync(stoppingToken).ConfigureAwait(false);
                    nextPurge = DateTimeOffset.UtcNow + _options.PurgeInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxProcessor encountered an error during a polling cycle.");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("OutboxProcessor stopped.");
    }

    /// <summary>
    /// Claims and delivers one batch of due entries. Returns the number claimed. Exposed for tests
    /// (deterministic, no timing).
    /// </summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IMessageOutboxStore>();
        var resolver = services.GetRequiredService<IEmailConfigResolver>();
        var transport = services.GetRequiredService<IEmailTransport>();

        var claimed = await store
            .ClaimDueAsync(_options.ClaimBatchSize, _options.LeaseDuration, cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in claimed)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await DeliverAsync(store, resolver, transport, entry, cancellationToken).ConfigureAwait(false);
        }

        return claimed.Count;
    }

    /// <summary>Purges expired terminal entries. Exposed for tests.</summary>
    internal async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMessageOutboxStore>();

        var removed = await store.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);
        if (removed > 0)
            logger.LogInformation("Outbox purged {Count} expired terminal entries.", removed);
    }

    private async Task DeliverAsync(
        IMessageOutboxStore store,
        IEmailConfigResolver resolver,
        IEmailTransport transport,
        OutboxEntry entry,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var config = await resolver.ResolveAsync(entry.Space, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No messaging relay is configured for space {entry.Space} at send time.");

            var transportName = entry.TransportName ?? "default";
            if (!config.Transports.TryGetValue(transportName, out var transportConfig))
                throw new InvalidOperationException(
                    $"Messaging transport '{transportName}' is not configured for space {entry.Space}.");

            await transport
                .SendAsync(ToResolvedTransport(transportConfig), entry.Message, cancellationToken)
                .ConfigureAwait(false);

            var retainUntil = _options.SentRetention > TimeSpan.Zero
                ? now + _options.SentRetention
                : (DateTimeOffset?)null;

            await store.MarkSentAsync(entry.MessageId, retainUntil, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Outbox message {MessageId} sent (space {Space}).", entry.MessageId, entry.Space);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down — leave the entry pending (its lease will expire and it retries)
        }
        catch (Exception ex)
        {
            var error = Truncate(ex.Message);
            var attemptsAfter = entry.Attempts + 1;

            // Finalize the failure even during shutdown (CancellationToken.None): the lease was consumed.
            if (attemptsAfter >= _options.MaxAttempts)
            {
                await store.MarkDeadLetterAsync(entry.MessageId, now + _options.DeadLetterTtl, error, CancellationToken.None)
                    .ConfigureAwait(false);
                logger.LogError(ex,
                    "Outbox message {MessageId} dead-lettered after {Attempts} attempts (space {Space}).",
                    entry.MessageId, attemptsAfter, entry.Space);
            }
            else
            {
                var nextAttemptAt = now + Backoff(entry.Attempts);
                await store.MarkForRetryAsync(entry.MessageId, nextAttemptAt, error, CancellationToken.None)
                    .ConfigureAwait(false);
                logger.LogWarning(ex,
                    "Outbox message {MessageId} attempt {Attempt} failed; retrying at {NextAttempt} (space {Space}).",
                    entry.MessageId, attemptsAfter, nextAttemptAt, entry.Space);
            }
        }
    }

    // Exponential backoff (base × 2^attempts), clamped to the cap. Computed in double to avoid overflow.
    private TimeSpan Backoff(int attempts)
    {
        var scaled = _options.RetryBackoffBase.Ticks * Math.Pow(2, attempts);
        var ticks = (long)Math.Min(_options.RetryBackoffCap.Ticks, scaled);
        return TimeSpan.FromTicks(ticks);
    }

    private static ResolvedTransport ToResolvedTransport(TransportConfig t) => new()
    {
        Host = t.Host,
        Port = t.Port,
        Security = t.Security,
        Auth = t.Auth,
        Credentials = t.Credentials,
        EnvelopeFrom = t.EnvelopeFrom,
    };

    private static string Truncate(string value)
        => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
