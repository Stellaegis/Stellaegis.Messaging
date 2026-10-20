using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Primitives;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// Resolves messaging configuration from the secrets vault, with a bounded in-memory cache.
/// Reads the single blob of the space and, for a compartment that has none, the shared space's blob
/// when <see cref="MessagingModule.InheritSharedSpace"/> is turned on.
/// </summary>
internal sealed class VaultEmailConfigResolver(
    ISecretsProvider secrets,
    MessagingConfigCache cache,
    bool inheritSharedSpace,
    IOptions<VaultConfigurationOptions> cacheOptions,
    ILogger<VaultEmailConfigResolver> logger) : IEmailConfigResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _ttl = cacheOptions.Value.ConfigCacheTtl;

    /// <inheritdoc />
    public async Task<MessagingConfig?> ResolveAsync(Space space, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(space);

        if (cache.TryGet(space, out var cached))
            return cached;

        var ownPath = MessagingSecretPaths.For(space);
        var config = await LoadAsync(ownPath, cancellationToken).ConfigureAwait(false);
        var sources = new List<SecretPath> { ownPath };

        if (!space.IsShared && inheritSharedSpace)
        {
            // The entry depends on both paths whichever one answered: the subtle case is a compartment
            // configuration added later, for an entry currently served by the shared space.
            var sharedPath = MessagingSecretPaths.For(Space.Shared);
            sources.Add(sharedPath);
            config ??= await LoadAsync(sharedPath, cancellationToken).ConfigureAwait(false);
        }

        if (config is null)
            return null;

        cache.Set(space, config, sources, _ttl);
        return config;
    }

    private async Task<MessagingConfig?> LoadAsync(SecretPath path, CancellationToken cancellationToken)
    {
        string secretJson;
        try
        {
            secretJson = await secrets.GetSecretAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        try
        {
            var config = JsonSerializer.Deserialize<MessagingConfig>(secretJson, JsonOptions)
                         ?? throw new InvalidOperationException(
                             $"Empty messaging configuration at vault path '{path}'.");

            logger.LogDebug("Resolved messaging config from vault: {VaultPath}", path);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid messaging configuration JSON at vault path '{path}'.", ex);
        }
    }
}
