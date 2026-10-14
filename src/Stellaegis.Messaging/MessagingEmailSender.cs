using Microsoft.Extensions.Logging;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;
using Stellaegis.MultiTenant.Context;

namespace Stellaegis.Messaging;

/// <summary>
/// Default <see cref="IEmailSender"/>: resolves the tenant messaging configuration, selects the
/// transport, validates and (if needed) degrades the From against the transport's from-policy,
/// then <b>enqueues</b> the finalized message on the <see cref="IMessageOutbox"/> for reliable delivery.
/// </summary>
/// <remarks>
/// The transport engine owns the transport; the developer owns the message. Config resolution
/// happens per send (like <c>TenantDbConnectionFactory</c>), never via static options.
/// The from-policy is applied here so the message is <i>frozen</i> before it enters the outbox; the
/// worker sends it verbatim and re-resolves the transport connection at send time.
/// The tenant context is optional: hosts that are not tenant-scoped (e.g. the Identity server,
/// which sends system emails) resolve the system scope (<c>messaging.default</c>) directly.
/// </remarks>
internal sealed class MessagingEmailSender(
    IEnumerable<ITenantContext> tenantContexts,
    IEmailConfigResolver configResolver,
    IMessageOutbox outbox,
    ILogger<MessagingEmailSender> logger) : IEmailSender
{
    private const string DefaultTransportName = "default";

    private readonly ITenantContext? _tenantContext = tenantContexts.FirstOrDefault();

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        // No ambient tenant (non-tenant host, or a system route) → system scope (messaging.default).
        var tenantSlug = _tenantContext is not null && _tenantContext.TryGetTenantSlug(out var slug)
            ? slug
            : MessagingScopes.System;

        return SendAsync(tenantSlug, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendAsync(string tenantSlug, EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(message);

        var config = await configResolver.ResolveAsync(tenantSlug, cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            // No relay configured yet (neither messaging.{tenant} nor messaging.default). The safe
            // default is to show the message, never to send one before the relay is set up.
            DevEmailLog.Write(logger, message, "no relay configured");
            return;
        }

        // Entry (if any) matching the desired From — carries an optional transport binding and reply-to.
        var boundEntry = message.From is { } from
            ? config.FromAddresses.FirstOrDefault(fa => AddressMatches(fa.Address, from.Address))
            : null;

        var transportName = message.TransportName ?? boundEntry?.Transport ?? DefaultTransportName;
        if (!config.Transports.TryGetValue(transportName, out var transportConfig))
            throw new InvalidOperationException(
                $"Messaging transport '{transportName}' is not configured for tenant '{tenantSlug}'.");

        var prepared = PrepareMessage(message, config, transportConfig, transportName, boundEntry, tenantSlug);

        // Hand the frozen message to the outbox; the worker delivers it (retries, dead-letter) and
        // re-resolves the transport connection at send time so no credential enters the buffer.
        await outbox.EnqueueAsync(tenantSlug, prepared, transportName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the effective From, applies the from-policy (degrade/reject/force) and reply-to,
    /// and returns the final message to hand to the transport.
    /// </summary>
    private EmailMessage PrepareMessage(
        EmailMessage message,
        MessagingConfig config,
        TransportConfig transportConfig,
        string transportName,
        FromAddress? boundEntry,
        string tenantSlug)
    {
        var replyTo = new List<EmailAddress>(message.ReplyTo);
        if (replyTo.Count == 0 && !string.IsNullOrWhiteSpace(boundEntry?.ReplyTo))
            replyTo.Add(new EmailAddress(boundEntry!.ReplyTo!));

        var desiredFrom = message.From ?? transportConfig.DefaultFrom ?? config.DefaultFrom
            ?? throw new InvalidOperationException(
                $"Message has no From and no defaultFrom is configured " +
                $"(transport '{transportName}', tenant '{tenantSlug}').");

        var policy = transportConfig.FromPolicy;
        var kind = policy?.Kind ?? FromPolicyKind.IpUnrestricted;

        EmailAddress effectiveFrom;
        var sender = message.Sender;

        switch (kind)
        {
            case FromPolicyKind.DomainAuthorized:
                if (IsDomainAuthorized(desiredFrom.Address, policy!))
                {
                    effectiveFrom = desiredFrom;
                }
                else if (transportConfig.OnFromMismatch == FromMismatchBehavior.Reject)
                {
                    throw new EmailRejectedException(
                        $"From '{desiredFrom.Address}' is not authorized by transport '{transportName}' " +
                        $"(authorized domains: {string.Join(", ", policy!.Domains)}).");
                }
                else
                {
                    // Degrade (never break): From ← default authorized address, desired name kept,
                    // real address moved to Reply-To.
                    var fallback = transportConfig.DefaultFrom ?? config.DefaultFrom
                        ?? throw new InvalidOperationException(
                            $"Cannot degrade From '{desiredFrom.Address}': transport '{transportName}' " +
                            "has no defaultFrom to fall back to.");
                    effectiveFrom = new EmailAddress(fallback.Address, desiredFrom.Name ?? fallback.Name);
                    AddReplyTo(replyTo, desiredFrom);
                    logger.LogWarning(
                        "From '{Desired}' degraded to '{Effective}' on transport '{Transport}' (tenant '{Tenant}').",
                        desiredFrom.Address, effectiveFrom.Address, transportName, tenantSlug);
                }

                sender = SanitizeSender(sender, policy!);
                break;

            case FromPolicyKind.MailboxBound:
                // From is forced to the connection mailbox; the desired address becomes Reply-To.
                var mailbox = transportConfig.DefaultFrom
                    ?? throw new InvalidOperationException(
                        $"MailboxBound transport '{transportName}' requires a defaultFrom (the mailbox address).");
                if (AddressMatches(mailbox.Address, desiredFrom.Address))
                {
                    effectiveFrom = desiredFrom;
                }
                else
                {
                    effectiveFrom = new EmailAddress(mailbox.Address, desiredFrom.Name ?? mailbox.Name);
                    AddReplyTo(replyTo, desiredFrom);
                }

                sender = null; // a mailbox-bound relay speaks only for its own mailbox
                break;

            case FromPolicyKind.IpUnrestricted:
            default:
                effectiveFrom = desiredFrom;
                break;
        }

        return message with
        {
            From = effectiveFrom,
            ReplyTo = replyTo,
            Sender = sender
        };
    }

    private static EmailAddress? SanitizeSender(EmailAddress? sender, FromPolicy policy)
        => sender is null || IsDomainAuthorized(sender.Address, policy) ? sender : null;

    private static void AddReplyTo(List<EmailAddress> replyTo, EmailAddress address)
    {
        if (!replyTo.Any(r => AddressMatches(r.Address, address.Address)))
            replyTo.Add(address);
    }

    private static bool IsDomainAuthorized(string address, FromPolicy policy)
    {
        var domain = DomainOf(address);
        return domain.Length > 0
            && policy.Domains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AddressMatches(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : string.Empty;
    }
}
