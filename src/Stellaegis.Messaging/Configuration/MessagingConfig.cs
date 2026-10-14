using System.Text.Json.Serialization;

namespace Stellaegis.Messaging.Configuration;

// Deserialized from the single vault blob of a space. JSON keys are camelCase; the resolver
// deserializes with PropertyNameCaseInsensitive = true.
//
// The [JsonConverter] attributes on the enums below are what the vault and the outbox store: their
// serializer options register no enum converter, so without the attributes the enums would persist as
// numbers and every stored configuration would become unreadable. They do NOT describe an HTTP API: a
// converter registered in JsonSerializerOptions.Converters takes precedence over the attribute, so a host
// with a global camelCase enum converter answers "startTls" or "domainAuthorized", never "StartTls".

/// <summary>TLS mode for an SMTP connection. Mapped to MailKit's SecureSocketOptions by the SMTP transport.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SmtpSecurity>))]
public enum SmtpSecurity
{
    /// <summary>No transport security (port 25 in private networks).</summary>
    None,

    /// <summary>Opportunistic TLS upgrade (port 587).</summary>
    StartTls,

    /// <summary>Implicit TLS on connect (port 465).</summary>
    SslOnConnect
}

/// <summary>Authentication scheme for an SMTP transport.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransportAuth>))]
public enum TransportAuth
{
    /// <summary>Username/password (or API key) authentication.</summary>
    Basic,

    /// <summary>Client certificate (TLS) authentication, e.g. an Exchange Online connector.</summary>
    ClientCert,

    /// <summary>No SMTP authentication — the relay trusts the source IP (e.g. an IP-filtered connector).</summary>
    None
}

/// <summary>Relay authorization regime governing which From addresses are allowed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FromPolicyKind>))]
public enum FromPolicyKind
{
    /// <summary>Any address within the verified domain(s) is allowed.</summary>
    DomainAuthorized,

    /// <summary>From is forced to the connection mailbox address.</summary>
    MailboxBound,

    /// <summary>No From filtering, as on a relay that trusts the source IP.</summary>
    IpUnrestricted
}

/// <summary>Behaviour when a message From is not allowed by the resolved transport's from-policy.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FromMismatchBehavior>))]
public enum FromMismatchBehavior
{
    /// <summary>Rewrite From to the transport default and move the desired address to Reply-To (never breaks).</summary>
    Degrade,

    /// <summary>Fail the send with an explicit error.</summary>
    Reject
}

/// <summary>Inline SMTP credentials carried by a transport (single-blob model, no vault indirection).</summary>
public sealed record TransportCredentials
{
    /// <summary>Basic auth username (or API key identifier).</summary>
    public string? Username { get; init; }

    /// <summary>Basic auth password (or API key).</summary>
    public string? Password { get; init; }

    /// <summary>Base64-encoded PFX for client-certificate authentication.</summary>
    public string? CertificatePfxBase64 { get; init; }

    /// <summary>Password protecting the PFX, if any.</summary>
    public string? CertificatePassword { get; init; }
}

/// <summary>Authorization policy for the From address of a transport.</summary>
public sealed record FromPolicy
{
    /// <summary>Regime governing From authorization.</summary>
    public required FromPolicyKind Kind { get; init; }

    /// <summary>Authorized domains for <see cref="FromPolicyKind.DomainAuthorized"/>.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];
}

/// <summary>A named connection transport (one SMTP relay identity).</summary>
public sealed record TransportConfig
{
    /// <summary>SMTP relay hostname.</summary>
    public required string Host { get; init; }

    /// <summary>SMTP port. Defaults to 587 (STARTTLS).</summary>
    public int Port { get; init; } = 587;

    /// <summary>Transport security mode.</summary>
    public SmtpSecurity Security { get; init; } = SmtpSecurity.StartTls;

    /// <summary>Authentication scheme.</summary>
    public TransportAuth Auth { get; init; } = TransportAuth.Basic;

    /// <summary>From-address authorization policy.</summary>
    public FromPolicy? FromPolicy { get; init; }

    /// <summary>Always-authorized address, used as the target when degrading a non-compliant From.</summary>
    public EmailAddress? DefaultFrom { get; init; }

    /// <summary>Envelope sender (Return-Path / SMTP MAIL FROM). SPF-critical — must belong to the relay.</summary>
    public string? EnvelopeFrom { get; init; }

    /// <summary>Behaviour on a From that violates <see cref="FromPolicy"/>. Defaults to degrade.</summary>
    public FromMismatchBehavior OnFromMismatch { get; init; } = FromMismatchBehavior.Degrade;

    /// <summary>Inline credentials for this transport.</summary>
    public TransportCredentials? Credentials { get; init; }
}

/// <summary>
/// A declared emission address. Optional overall: absent when the relay authorizes a whole domain
/// (From is free within it, e.g. sourced from business data). Present to control local-parts on a
/// shared relay, or to bind an address to a dedicated transport (a mailbox-bound relay, per-address
/// credentials).
/// </summary>
public sealed record FromAddress
{
    /// <summary>
    /// Full emission address. On a shared system relay the domain is fixed and only the local-part
    /// is editable in the UI; storage always keeps the complete address.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>Transport name to use for this address. Null → the "default" transport.</summary>
    public string? Transport { get; init; }

    /// <summary>Reply-To applied when the message sets none.</summary>
    public string? ReplyTo { get; init; }
}

/// <summary>
/// Messaging configuration of one space (a compartment, or the shared space),
/// stored as a single vault blob with inline credentials.
/// </summary>
public sealed record MessagingConfig
{
    /// <summary>Named connection transports. The "default" key is the primary/inherited transport.</summary>
    public IReadOnlyDictionary<string, TransportConfig> Transports { get; init; }
        = new Dictionary<string, TransportConfig>();

    /// <summary>
    /// Optional allow-list / transport binding of emission addresses.
    /// Empty when From is free within an authorized domain (see <see cref="FromAddress"/>).
    /// </summary>
    public IReadOnlyList<FromAddress> FromAddresses { get; init; } = [];

    /// <summary>Default From when a message specifies none (system / anonymous mail).</summary>
    public EmailAddress? DefaultFrom { get; init; }
}
