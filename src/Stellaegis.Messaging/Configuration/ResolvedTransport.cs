namespace Stellaegis.Messaging.Configuration;

/// <summary>
/// A fully resolved transport, ready for the SMTP layer to open a connection and send.
/// Produced by the orchestrator from a <see cref="TransportConfig"/> once the transport has been
/// selected; carries no vault knowledge and no policy decisions (those are already applied to the message).
/// </summary>
public sealed record ResolvedTransport
{
    /// <summary>SMTP relay hostname.</summary>
    public required string Host { get; init; }

    /// <summary>SMTP port.</summary>
    public required int Port { get; init; }

    /// <summary>Transport security mode.</summary>
    public required SmtpSecurity Security { get; init; }

    /// <summary>Authentication scheme.</summary>
    public required TransportAuth Auth { get; init; }

    /// <summary>Inline credentials, or null for IP-trusted relays.</summary>
    public TransportCredentials? Credentials { get; init; }

    /// <summary>Envelope sender (Return-Path / SMTP MAIL FROM). SPF-critical — belongs to the relay.</summary>
    public string? EnvelopeFrom { get; init; }
}
