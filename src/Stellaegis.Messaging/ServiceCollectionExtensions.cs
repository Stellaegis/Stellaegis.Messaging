using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives;
using Stellaegis.Primitives.DependencyInjection;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;

namespace Stellaegis.Messaging;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email engine: the orchestrating <see cref="IEmailSender"/> that selects the transport
    /// and applies its from-policy, and the outbox that delivers through the registered
    /// <see cref="IEmailTransport"/>. Reads <see cref="MessagingOptions"/> from the <c>Stellaegis:Messaging</c>
    /// section.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="MessagingModule"/> with compartments: the application declares a frame with compartments,
    /// <c>ApplicationFrame.SingleCompartment</c> or <c>ManyCompartments</c>, and calls this method once.
    /// The host also registers where the configuration comes from — <see cref="AddStellaegisEmailConfiguration"/>
    /// for a configuration section, or the vault of <c>Stellaegis.Messaging.Secrets</c> — and a transport,
    /// such as the SMTP transport of <c>Stellaegis.Messaging.MailKit</c>.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmail(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingOptions.SectionName)
    {
        services.AddStellaegisModule(ModuleDeclaration.Compartmented<MessagingModule>());
        services.Configure<MessagingOptions>(configuration.GetSection(sectionName));
        services.AddStellaegisEmailOutbox(configuration);
        services.TryAddScoped<IEmailSender, MessagingEmailSender>();
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IEmailConfigResolver"/> that reads one <see cref="MessagingConfig"/> from a
    /// configuration section, <c>Stellaegis:Messaging:Configuration</c> by default. Suited to an application
    /// with a single relay: each field can come from its own configuration source, a password from an
    /// environment variable for instance.
    /// </summary>
    /// <remarks>
    /// The section is the configuration of the shared space. A compartment uses it only when
    /// <see cref="MessagingOptions.InheritSharedSpace"/> is set. The section is read again at each send, so a
    /// reloaded configuration file takes effect without a restart. An absent section is the "no relay
    /// configured yet" state: messages are written to the log, never sent.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = ConfigurationEmailConfigResolver.SectionName)
    {
        services.TryAddSingleton<IEmailConfigResolver>(sp => new ConfigurationEmailConfigResolver(
            configuration.GetSection(sectionName),
            sp.GetRequiredService<IOptions<MessagingOptions>>()));
        return services;
    }

    /// <summary>
    /// Registers the reliable outbox: the in-memory <see cref="IMessageOutboxStore"/> (a single instance
    /// serving both producers and the worker) and the <see cref="OutboxProcessor"/> background worker.
    /// Hosts that need durability register the PostgreSQL store, which replaces the in-memory one.
    /// Reads options from <c>Stellaegis:Messaging:Outbox</c>.
    /// </summary>
    /// <remarks>
    /// Wired automatically by <see cref="AddStellaegisEmail"/>. The worker resolves the host's
    /// <see cref="IEmailTransport"/> per cycle, so register a transport (e.g. the SMTP transport) too.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailOutbox(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingOutboxOptions.SectionName)
    {
        services.Configure<MessagingOutboxOptions>(configuration.GetSection(sectionName));

        services.TryAddSingleton<InMemoryMessageOutboxStore>();
        services.TryAddSingleton<IMessageOutboxStore>(sp => sp.GetRequiredService<InMemoryMessageOutboxStore>());
        services.TryAddSingleton<IMessageOutbox>(sp => sp.GetRequiredService<IMessageOutboxStore>());

        services.AddHostedService<OutboxProcessor>();
        return services;
    }

    /// <summary>
    /// Registers a development-only <see cref="IEmailSender"/> that logs email content
    /// to the console instead of sending via SMTP. Do not use in production.
    /// </summary>
    public static IServiceCollection AddStellaegisLoggingEmail(this IServiceCollection services)
    {
        services.TryAddTransient<IEmailSender, LoggingEmailSender>();
        return services;
    }
}
