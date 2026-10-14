using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Sends transactional email messages.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends <paramref name="message"/> with the messaging configuration of <paramref name="space"/>: a
    /// compartment, or the shared space for mail that belongs to none.
    /// </summary>
    Task SendAsync(Space space, EmailMessage message, CancellationToken cancellationToken = default);
}
