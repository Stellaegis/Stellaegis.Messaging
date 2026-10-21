using Microsoft.Extensions.Logging;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Vault-backed <see cref="IMessagingConfigStore"/>. Reads/writes the single blob of a space
/// via <see cref="IMutableSecretsProvider"/>, masking secret credential fields on read and merging
/// them back on write.
/// </summary>
internal sealed class VaultMessagingConfigStore(
    IMutableSecretsProvider secrets,
    MessagingConfigCache cache,
    ILogger<VaultMessagingConfigStore> logger) : IMessagingConfigStore
{
    /// <inheritdoc />
    public async Task<MessagingConfig?> GetAsync(Space space, CancellationToken cancellationToken = default)
    {
        var path = MessagingSecretPaths.For(Guard.NotNull(space));

        var config = await MessagingConfigJson.ReadAsync(secrets, path, cancellationToken).ConfigureAwait(false);
        return config is null ? null : MaskSecrets(config);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Space space, MessagingConfig config, CancellationToken cancellationToken = default)
    {
        var path = MessagingSecretPaths.For(Guard.NotNull(space));
        Guard.NotNull(config);

        var existing = await MessagingConfigJson.ReadAsync(secrets, path, cancellationToken).ConfigureAwait(false);
        var merged = MergeSecrets(config, existing);

        await secrets.SetSecretAsync(path, MessagingConfigJson.Write(merged), cancellationToken).ConfigureAwait(false);
        cache.Invalidate(path);

        logger.LogInformation("Saved messaging configuration for space {Space}.", space);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Space space, CancellationToken cancellationToken = default)
    {
        var path = MessagingSecretPaths.For(Guard.NotNull(space));

        await secrets.DeleteSecretAsync(path, cancellationToken).ConfigureAwait(false);
        cache.Invalidate(path);

        logger.LogInformation("Deleted messaging configuration for space {Space}.", space);
    }

    private static MessagingConfig MaskSecrets(MessagingConfig config)
    {
        var transports = config.Transports.ToDictionary(
            kv => kv.Key,
            kv => kv.Value with { Credentials = MaskCredentials(kv.Value.Credentials) });

        return config with { Transports = transports };
    }

    private static MessagingConfig MergeSecrets(MessagingConfig incoming, MessagingConfig? existing)
    {
        var transports = new Dictionary<string, TransportConfig>(incoming.Transports.Count);
        foreach (var (name, transport) in incoming.Transports)
        {
            TransportConfig? existingTransport = null;
            existing?.Transports.TryGetValue(name, out existingTransport);

            transports[name] = transport with
            {
                Credentials = MergeCredentials(transport.Credentials, existingTransport?.Credentials)
            };
        }

        return incoming with { Transports = transports };
    }

    private static TransportCredentials? MaskCredentials(TransportCredentials? credentials)
        => credentials is null
            ? null
            : credentials with
            {
                Password = Mask(credentials.Password),
                CertificatePfxBase64 = Mask(credentials.CertificatePfxBase64),
                CertificatePassword = Mask(credentials.CertificatePassword)
            };

    private static TransportCredentials? MergeCredentials(TransportCredentials? incoming, TransportCredentials? existing)
        => incoming is null
            ? existing
            : incoming with
            {
                Password = MergeSecret(incoming.Password, existing?.Password),
                CertificatePfxBase64 = MergeSecret(incoming.CertificatePfxBase64, existing?.CertificatePfxBase64),
                CertificatePassword = MergeSecret(incoming.CertificatePassword, existing?.CertificatePassword)
            };

    private static string? Mask(string? value)
        => string.IsNullOrEmpty(value) ? value : MessagingConfigMasking.MaskedSecret;

    private static string? MergeSecret(string? incoming, string? existing)
        => string.IsNullOrEmpty(incoming) || incoming == MessagingConfigMasking.MaskedSecret ? existing : incoming;
}
