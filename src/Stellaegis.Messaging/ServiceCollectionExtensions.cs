using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email engine with an in-memory outbox: the orchestrating <see cref="IEmailSender"/> that
    /// selects the transport and applies its from-policy, the outbox worker that delivers through the registered
    /// <see cref="IEmailTransport"/>, and the <see cref="InMemoryMessageOutboxStore"/>, which loses the messages
    /// waiting for a retry when the process stops. A host that needs durability calls
    /// <c>AddStellaegisEmailPostgreSql</c> of <c>Stellaegis.Messaging.PostgreSQL</c> instead, once.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="MessagingModule"/>: the application declares a frame with compartments,
    /// <c>ApplicationFrame.SingleCompartment</c> or <c>ManyCompartments</c>, and calls this method once.
    /// The host also registers where the configuration comes from — <see cref="AddStellaegisEmailConfiguration"/>
    /// for a configuration section, or the vault of <c>Stellaegis.Messaging.Secrets</c> — and a transport,
    /// such as the SMTP transport of <c>Stellaegis.Messaging.MailKit</c>. Without either, the host fails to start,
    /// naming the registration to add.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<InMemoryMessageOutboxStore>();
        services.TryAddSingleton<IMessageOutboxStore>(sp => sp.GetRequiredService<InMemoryMessageOutboxStore>());
        return services.AddStellaegisEmailServices(configuration);
    }

    /// <summary>
    /// Registers the email engine over the <see cref="IMessageOutboxStore"/> an adapter registers: the module
    /// declaration, the outbox options, the startup check, the worker and the <see cref="IEmailSender"/>.
    /// Called by <see cref="AddStellaegisEmail"/> and by the registration of a durable outbox store, which a host
    /// calls one of, once: the module is declared by each, and a module declared twice fails the startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The module is declared with compartments, accepting the shared space — mail that belongs to no compartment is
    /// sent under it —, with the capability <see cref="MessagingModule.InheritSharedSpace"/>. It declares the
    /// compatibility with an isolation by owner (<see cref="ModuleCapability.OwnerIsolation"/>) from here, by
    /// nature: no message of the outbox belongs to an actor. A message belongs to a space, is read by the sending
    /// engine alone, and is deleted once sent, so the isolation holds without a rule of the store.
    /// </para>
    /// <para>
    /// Reads the outbox options from <c>Stellaegis:Messaging:Outbox</c> (<see cref="MessagingOutboxOptions"/>),
    /// validated when the host starts. To set one from code, follow with
    /// <c>services.PostConfigure&lt;MessagingOutboxOptions&gt;(o =&gt; ...)</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        Guard.NotNull(services);
        Guard.NotNull(configuration);

        services.AddStellaegisModule(ModuleDeclaration.Compartmented<MessagingModule>()
            .AcceptingSharedSpace()
            .WithCapability(MessagingModule.InheritSharedSpace)
            .WithCapability(ModuleCapability.OwnerIsolation));

        services.AddStellaegisOptions<MessagingOutboxOptions>(configuration, MessagingOutboxOptions.SectionName);

        // Before the outbox worker: hosted services start in registration order.
        services.AddHostedService<MessagingStartupCheck>();

        services.TryAddSingleton<IMessageOutbox>(sp => sp.GetRequiredService<IMessageOutboxStore>());
        services.TryAddSingleton<IMessageOutboxPurge>(sp => sp.GetRequiredService<IMessageOutboxStore>());
        services.AddHostedService<OutboxProcessor>();

        services.TryAddScoped<IEmailSender, MessagingEmailSender>();
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IEmailConfigResolver"/> that reads the <see cref="MessagingConfig"/> of a space from
    /// a configuration section, <c>Stellaegis:Messaging</c> by default, read against the frame of the application.
    /// Suited to an application whose relays are deployed with it: each field can come from its own configuration
    /// source, a password from an environment variable for instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In an application with a single compartment, <c>{section}:Configuration</c> describes that compartment. In an
    /// application with several, it describes the shared space, and <c>{section}:Compartments:{compartment}</c> a
    /// compartment; a compartment without a configuration of its own uses the shared one only when
    /// <see cref="MessagingModule.InheritSharedSpace"/> is turned on.
    /// </para>
    /// <para>
    /// The section is read again at each send, so a reloaded configuration file takes effect without a restart.
    /// An absent section is the "no relay configured yet" state: messages are written to the log, never sent.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = ConfigurationEmailConfigResolver.SectionName)
    {
        Guard.NotNull(services);
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);

        services.TryAddSingleton<IEmailConfigResolver>(sp => new ConfigurationEmailConfigResolver(
            configuration.GetSection(sectionName),
            sp.GetStellaegisFrame(),
            sp.InheritsSharedSpace()));
        return services;
    }

    /// <summary>
    /// Whether the application turned on <see cref="MessagingModule.InheritSharedSpace"/>. Read from the module's
    /// declaration, which <see cref="AddStellaegisEmailServices"/> registers.
    /// </summary>
    public static bool InheritsSharedSpace(this IServiceProvider services)
        => Guard.NotNull(services).GetStellaegisModule<MessagingModule>()?.Parameters.Contains(MessagingModule.InheritSharedSpace) == true;

    /// <summary>
    /// Registers a development-only <see cref="IEmailSender"/> that writes email content to the log instead of
    /// sending it. Do not use in production.
    /// </summary>
    public static IServiceCollection AddStellaegisLoggingEmail(this IServiceCollection services)
    {
        services.TryAddTransient<IEmailSender, LoggingEmailSender>();
        return services;
    }
}
