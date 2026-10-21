using System.Text.Json;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging.Secrets;

/// <summary>
/// The single blob of a space in the vault, as JSON: read by the resolver and by the store alike, written by the
/// store. Property names are read without regard to case; written in camel case.
/// </summary>
internal static class MessagingConfigJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the configuration at <paramref name="path"/>, or <see langword="null"/> when the vault holds no
    /// secret there.
    /// </summary>
    /// <exception cref="InvalidOperationException">The secret is not a messaging configuration.</exception>
    public static async Task<MessagingConfig?> ReadAsync(ISecretsProvider secrets, SecretPath path, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await secrets.GetSecretAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (SecretNotFoundException)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MessagingConfig>(json, ReadOptions)
                   ?? throw new InvalidOperationException($"Empty messaging configuration at vault path '{path}'.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid messaging configuration JSON at vault path '{path}'.", ex);
        }
    }

    /// <summary>The blob to write for <paramref name="config"/>.</summary>
    public static string Write(MessagingConfig config) => JsonSerializer.Serialize(config, WriteOptions);
}
