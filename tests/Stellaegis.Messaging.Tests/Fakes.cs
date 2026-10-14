using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging.Tests;

/// <summary>Records deliveries and can simulate transient or permanent transport failures.</summary>
internal sealed class FakeEmailTransport : IEmailTransport
{
    private readonly object _gate = new();

    /// <summary>Number of leading attempts that fail before a success (transient failure simulation).</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every attempt throws (permanent failure simulation).</summary>
    public bool AlwaysFail { get; set; }

    /// <summary>Messages that were successfully "sent".</summary>
    public List<EmailMessage> Sent { get; } = [];

    /// <summary>Total number of send attempts (successful or not).</summary>
    public int Attempts { get; private set; }

    public Task SendAsync(ResolvedTransport transport, EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Attempts++;
            if (AlwaysFail || Attempts <= FailuresBeforeSuccess)
                throw new InvalidOperationException($"simulated transport failure #{Attempts}");
            Sent.Add(message);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Returns a fixed configuration (or null = "no relay configured") for any tenant.</summary>
internal sealed class FakeEmailConfigResolver(MessagingConfig? config) : IEmailConfigResolver
{
    public Task<MessagingConfig?> ResolveAsync(string tenantSlug, CancellationToken cancellationToken = default)
        => Task.FromResult(config);

    /// <summary>A minimal config with a single "default" transport that authorizes any From.</summary>
    public static MessagingConfig WithDefaultTransport() => new()
    {
        Transports = new Dictionary<string, TransportConfig>
        {
            ["default"] = new TransportConfig
            {
                Host = "smtp.example.test",
                Port = 587,
                DefaultFrom = new EmailAddress("noreply@example.test", "Example"),
                FromPolicy = new FromPolicy { Kind = FromPolicyKind.IpUnrestricted },
            },
        },
    };
}
