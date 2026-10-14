namespace Stellaegis.Messaging.Templates;

/// <summary>
/// Configuration for system email templates.
/// Templates are loaded from disk first, falling back to embedded defaults.
/// </summary>
public sealed class SystemTemplateOptions
{
    /// <summary>Configuration section name in <c>appsettings</c>.</summary>
    public const string SectionName = "Stellaegis:Messaging:Templates";

    /// <summary>
    /// Root folder for system templates on disk.
    /// Template names map to relative paths: <c>{SystemPath}/{name}.html</c>.
    /// </summary>
    public string SystemPath { get; init; } = "templates/system";
}
