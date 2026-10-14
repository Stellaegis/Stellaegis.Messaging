namespace Stellaegis.Messaging.Configuration;

/// <summary>Well-known messaging configuration scopes.</summary>
public static class MessagingScopes
{
    /// <summary>The system-wide default scope (<c>messaging.default</c>), defined by the SaaS developer.</summary>
    public const string System = "default";
}

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

/// <summary>Builds vault paths for messaging configuration blobs.</summary>
internal static class MessagingVaultPath
{
    /// <summary>
    /// Builds the vault path for a scope: internal <c>messaging.{scope}</c> with dots replaced
    /// by the provider separator (e.g. <c>messaging/acme</c>).
    /// </summary>
    public static string For(string scope, string separator)
        => $"messaging.{scope}".Replace(".", separator, StringComparison.Ordinal);
}
