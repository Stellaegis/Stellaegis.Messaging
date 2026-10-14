using System.Reflection;
using Microsoft.Extensions.Options;

namespace Stellaegis.Messaging.Templates;

/// <summary>
/// Loads templates from disk first, falling back to embedded resources.
/// Disk path: <c>{SystemPath}/{templateName}.html</c>.
/// Embedded path: <c>Stellaegis.Messaging.Templates.Defaults.{templateName}.html</c>
/// (slashes replaced with dots).
/// </summary>
internal sealed class SystemTemplateProvider(IOptions<SystemTemplateOptions> options) : ISystemTemplateProvider
{
    private static readonly Assembly ResourceAssembly = typeof(SystemTemplateProvider).Assembly;

    public async Task<string> GetTemplateAsync(string templateName, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(options.Value.SystemPath, $"{templateName}.html");

        if (File.Exists(filePath))
            return await File.ReadAllTextAsync(filePath, cancellationToken);

        var resourceName = $"Stellaegis.Messaging.Templates.Defaults.{templateName.Replace('/', '.')}.html";
        var stream = ResourceAssembly.GetManifestResourceStream(resourceName);

        if (stream is null)
            throw new FileNotFoundException(
                $"System template '{templateName}' not found on disk ({filePath}) or as embedded resource ({resourceName}).");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
