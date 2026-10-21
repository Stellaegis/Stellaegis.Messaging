using Stellaegis.Primitives;

namespace Stellaegis.Messaging;

/// <summary>
/// Thrown when a message <c>From</c> is not allowed by the resolved transport's from-policy and the transport is
/// configured to reject (rather than degrade) such messages. A <see cref="UserFacingException"/> with the code
/// <see cref="MessagingErrors.SenderRejected"/>: a validation error, which a host answers as such.
/// </summary>
public sealed class EmailRejectedException : UserFacingException
{
    /// <summary>Initializes a new instance of the <see cref="EmailRejectedException"/> class.</summary>
    public EmailRejectedException(string message)
        : base(Error.Validation(MessagingErrors.SenderRejected, message))
    {
    }
}
