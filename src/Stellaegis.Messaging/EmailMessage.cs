namespace Stellaegis.Messaging;

/// <summary>
/// An email address with an optional display name.
/// </summary>
/// <param name="Address">The email address (e.g. <c>compta@example.com</c>).</param>
/// <param name="Name">Optional display name (e.g. <c>Josiane Duchmol</c>). Free text, per message.</param>
public sealed record EmailAddress(string Address, string? Name = null);

/// <summary>
/// Represents an outbound email message.
/// </summary>
public sealed record EmailMessage
{
    /// <summary>
    /// Sender address. When null, the transport applies its configured default sender.
    /// </summary>
    public EmailAddress? From { get; init; }

    /// <summary>
    /// Envelope sender (<c>Sender</c> header), distinct from <see cref="From"/> for shared mailboxes.
    /// Optional.
    /// </summary>
    public EmailAddress? Sender { get; init; }

    /// <summary>Primary recipients. At least one is required.</summary>
    public required IReadOnlyList<EmailAddress> To { get; init; }

    /// <summary>Carbon-copy recipients.</summary>
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    /// <summary>Blind carbon-copy recipients.</summary>
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    /// <summary>Reply-to addresses.</summary>
    public IReadOnlyList<EmailAddress> ReplyTo { get; init; } = [];

    /// <summary>Email subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>HTML body. Required — all modern email clients render HTML.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Plain-text fallback. Displayed by clients that do not render HTML.
    /// Recommended for deliverability (spam filters favour multipart messages).
    /// </summary>
    public string? TextBody { get; init; }

    /// <summary>
    /// Optional override selecting a named transport from the tenant configuration.
    /// When null, the transport is selected from the From address binding, then the "default" transport.
    /// </summary>
    public string? TransportName { get; init; }

    /// <summary>
    /// Optional idempotency key. When set, a second enqueue with the same key for the same tenant is
    /// neutralized by the outbox (returns the existing message id) — so a retried caller does not send
    /// a duplicate. Not a MIME header: it is control metadata, never transmitted to the relay.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
