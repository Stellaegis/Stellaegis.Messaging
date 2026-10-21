namespace Stellaegis.Messaging;

/// <summary>
/// The codes of the errors the sending engine raises, each on a <see cref="Primitives.UserFacingException"/>, so
/// that a caller recognizes them without reading a message.
/// </summary>
public static class MessagingErrors
{
    /// <summary>
    /// The <c>From</c> of the message is not authorized by the transport's sender-address policy, and the transport
    /// rejects rather than degrades. Raised as <see cref="EmailRejectedException"/>.
    /// </summary>
    public const string SenderRejected = "Email.SenderRejected";

    /// <summary>The message names a transport that the configuration of the space does not hold.</summary>
    public const string TransportUnknown = "Email.TransportUnknown";

    /// <summary>The message has no <c>From</c>, and neither its transport nor the configuration has a default one.</summary>
    public const string FromRequired = "Email.FromRequired";

    /// <summary>
    /// The configuration of the transport cannot apply its own policy: a transport that degrades, or a
    /// mailbox-bound one, without a <c>DefaultFrom</c>. A fault of the configuration, not of the caller.
    /// </summary>
    public const string TransportMisconfigured = "Email.TransportMisconfigured";
}
