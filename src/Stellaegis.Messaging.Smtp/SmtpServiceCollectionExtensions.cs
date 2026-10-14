using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Smtp;
using Stellaegis.Secrets;

namespace Stellaegis.Messaging;

/// <summary>
/// Dependency injection helpers for the SMTP transport (MailKit).
/// </summary>
public static class SmtpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SMTP <see cref="IEmailTransport"/> (MailKit, Basic + client-certificate auth).
    /// Combine with <c>AddStellaegisEmail</c> to run the full messaging engine
    /// (orchestrator + vault config resolver + this transport).
    /// </summary>
    public static IServiceCollection AddStellaegisSmtpTransport(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailTransport, SmtpEmailTransport>();
        return services;
    }

    /// <summary>
    /// Auto-selects the email pipeline based on whether a secrets provider is available:
    /// <list type="bullet">
    ///   <item>an <see cref="ISecretsProvider"/> is registered (vault wired) → the vault-backed engine
    ///     (<c>AddStellaegisEmail</c> + SMTP transport). When no relay is configured yet, the engine
    ///     degrades to logging rather than sending;</item>
    ///   <item>no secrets provider (dev/InMemory, no vault) → the console <see cref="LoggingEmailSender"/>.</item>
    /// </list>
    /// Call this <b>after</b> registering the host's secrets provider (if any), so the selection sees it.
    /// Suitable as the default for starter kits: clone + F5 logs emails, never sends before setup.
    /// </summary>
    public static IServiceCollection AddStellaegisEmailAutoSelect(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingOptions.SectionName)
    {
        var hasSecrets = services.Any(d => d.ServiceType == typeof(ISecretsProvider));
        if (hasSecrets)
        {
            services.AddStellaegisEmail(configuration, sectionName);
            services.AddStellaegisSmtpTransport();
        }
        else
        {
            services.AddStellaegisLoggingEmail();
        }

        return services;
    }
}
