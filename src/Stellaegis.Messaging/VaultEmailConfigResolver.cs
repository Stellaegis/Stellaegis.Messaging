using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Caching;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging;

/// <summary>
/// Resolves messaging configuration from the secrets vault, with a bounded in-memory cache.
/// Reads the single per-perimeter blob (<c>messaging.{tenantSlug}</c>) and, when absent,
/// falls back to the system scope (<c>messaging.default</c>). Mirrors the vault access of
/// <c>TenantDbConnectionFactory</c> (dotted internal path → provider separator, bounded cache with
/// backplane invalidation).
/// </summary>
internal sealed class VaultEmailConfigResolver(
    ISecretsProvider secrets,
    [FromKeyedServices(MessagingOptions.SecretCacheKey)] BoundedCache cache,
    [FromKeyedServices(MessagingOptions.SecretCacheKey)] CacheSegmentInvalidator cacheInvalidator,
    IOptions<MessagingOptions> options,
    ILogger<VaultEmailConfigResolver> logger) : IEmailConfigResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MessagingOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<MessagingConfig?> ResolveAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var cacheKey = $"messaging:config:{tenantSlug}";
        if (cache.TryGet(cacheKey, out MessagingConfig? cached) && cached is not null)
            return cached;

        // messaging.{tenant} → fallback messaging.default (system scope, defined by the SaaS developer).
        // Absent from both scopes → null: no relay configured yet, the caller degrades to logging.
        var config = await LoadAsync(tenantSlug, cancellationToken).ConfigureAwait(false)
                     ?? await LoadAsync(MessagingScopes.System, cancellationToken).ConfigureAwait(false);

        if (config is null)
            return null;

        // Link the entry to BOTH candidate vault paths so a secret.changed event evicts it immediately,
        // whichever blob produced (or will produce) the value: the tenant blob, the system-default blob,
        // or — the subtle case — a tenant override added later for an entry currently served by default.
        var tokens = new[]
        {
            cacheInvalidator.GetToken(MessagingVaultPath.For(tenantSlug, secrets.Separator)),
            cacheInvalidator.GetToken(MessagingVaultPath.For(MessagingScopes.System, secrets.Separator)),
        };
        cache.Set(cacheKey, config, tokens, ttl: _options.SecretCacheTtl);
        return config;
    }

    private async Task<MessagingConfig?> LoadAsync(string scope, CancellationToken cancellationToken)
    {
        var vaultPath = MessagingVaultPath.For(scope, secrets.Separator);

        string secretJson;
        try
        {
            secretJson = await secrets.GetSecretAsync(vaultPath, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null; // scope not configured — the caller falls back to the system scope
        }

        try
        {
            var config = JsonSerializer.Deserialize<MessagingConfig>(secretJson, JsonOptions)
                         ?? throw new InvalidOperationException(
                             $"Empty messaging configuration at vault path '{vaultPath}'.");

            logger.LogDebug("Resolved messaging config from vault: {VaultPath}", vaultPath);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid messaging configuration JSON at vault path '{vaultPath}'.", ex);
        }
    }
}
