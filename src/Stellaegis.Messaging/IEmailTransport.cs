using Stellaegis.Messaging.Configuration;

namespace Stellaegis.Messaging;

/// <summary>
/// Low-level transport that opens a connection to a relay and sends one message.
/// Stateless with respect to space and vault: it receives an already-resolved
/// <see cref="ResolvedTransport"/> per call. Implemented per protocol (SMTP in Stellaegis.Messaging.Smtp).
/// </summary>
/// <remarks>
/// From-policy validation and degradation are applied by the orchestrating <see cref="IEmailSender"/>
/// before the message reaches the transport; the message passed here is final.
/// </remarks>
public interface IEmailTransport
{
    /// <summary>Sends <paramref name="message"/> using the resolved <paramref name="transport"/>.</summary>
    Task SendAsync(
        ResolvedTransport transport,
        EmailMessage message,
        CancellationToken cancellationToken = default);
}
