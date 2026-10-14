namespace Stellaegis.Messaging.Templates;

/// <summary>
/// Loads system templates by name.
/// Resolution order: filesystem (<see cref="SystemTemplateOptions.SystemPath"/>),
/// then embedded resources (framework defaults).
/// </summary>
public interface ISystemTemplateProvider
{
    /// <summary>
    /// Returns the raw Scriban template content for the given name.
    /// </summary>
    /// <param name="templateName">
    /// Slash-separated template path, e.g. <c>identity/email-verification</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template content string.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when neither a file on disk nor an embedded resource is found.
    /// </exception>
    Task<string> GetTemplateAsync(string templateName, CancellationToken cancellationToken = default);
}
