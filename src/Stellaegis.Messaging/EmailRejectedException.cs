namespace Stellaegis.Messaging;

/// <summary>
/// Thrown when a message From is not allowed by the resolved transport's from-policy
/// and the transport is configured to reject (rather than degrade) such messages.
/// </summary>
public sealed class EmailRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EmailRejectedException"/> class.</summary>
    public EmailRejectedException(string message) : base(message)
    {
    }
}
