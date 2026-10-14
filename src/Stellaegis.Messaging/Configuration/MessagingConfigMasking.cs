namespace Stellaegis.Messaging.Configuration;

/// <summary>Masking convention for sensitive credential fields exposed by <c>IMessagingConfigStore</c>.</summary>
public static class MessagingConfigMasking
{
    /// <summary>
    /// Sentinel returned in place of a stored secret when reading through the config store.
    /// On save, a field still equal to this sentinel (or empty) is treated as "unchanged"
    /// and merged with the stored value rather than overwriting it.
    /// </summary>
    public const string MaskedSecret = "••••••••";
}
