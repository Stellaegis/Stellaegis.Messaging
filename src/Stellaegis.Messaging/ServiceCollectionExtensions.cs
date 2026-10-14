using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stellaegis.Backplane.SecretEvents;
using Stellaegis.Caching;
using Stellaegis.Messaging.Configuration;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Messaging.Templates;

namespace Stellaegis.Messaging;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vault-backed <see cref="IEmailConfigResolver"/> (single blob per perimeter,
    /// <c>messaging.{tenant}</c> → <c>messaging.default</c>) and its bounded, invalidatable cache.
    /// Requires an <c>ISecretsProvider</c> to be registered by the host.
    /// Reads options from the <c>Stellaegis:Messaging</c> section.
    /// </summary>
    /// <remarks>
    /// The resolved configuration is cached in a keyed <see cref="BoundedCache"/> with segment
    /// invalidation by vault path: a config edit publishes a <c>secret.changed</c> event that evicts the
    /// affected perimeter, so the cache TTL can be long. Hosts that send mail should call
    /// <c>UseStellaegisMessagingSecretsBackplane()</c> at startup to wire the subscription (mirrors the
    /// tenant DB secret cache); without it, the TTL is the sole freshness mechanism.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailConfigResolver(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingOptions.SectionName)
    {
        services.Configure<MessagingOptions>(configuration.GetSection(sectionName));

        services.AddStellaegisBoundedCache(MessagingOptions.SecretCacheKey, sp =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            return new BoundedCacheOptions
            {
                SizeLimit = options.SecretCacheSizeLimit,
                Expiration = options.SecretCacheTtl,
            };
        });
        // Segment invalidator for the config cache: entries are keyed to their vault path(s), so a
        // backplane secret.changed event evicts exactly the affected entry (see UseStellaegisMessagingSecretsBackplane).
        services.TryAddKeyedSingleton<CacheSegmentInvalidator>(MessagingOptions.SecretCacheKey);
        services.AddStellaegisSecretsBackplaneEvents();

        services.TryAddScoped<IEmailConfigResolver, VaultEmailConfigResolver>();
        return services;
    }

    /// <summary>
    /// Registers the production email engine: the vault config resolver (<see cref="IEmailConfigResolver"/>)
    /// and the orchestrating <see cref="IEmailSender"/> that selects the transport, validates/degrades the
    /// From, and delegates to the registered <see cref="IEmailTransport"/>.
    /// The host must also register an <see cref="IEmailTransport"/> (e.g. the SMTP transport),
    /// an <c>ISecretsProvider</c> and an <c>ITenantContext</c>.
    /// </summary>
    public static IServiceCollection AddStellaegisEmail(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingOptions.SectionName)
    {
        services.AddStellaegisEmailConfigResolver(configuration, sectionName);
        services.AddStellaegisEmailOutbox(configuration);
        services.TryAddScoped<IEmailSender, MessagingEmailSender>();
        return services;
    }

    /// <summary>
    /// Registers the reliable outbox: the in-memory <see cref="IMessageOutboxStore"/> (a single instance
    /// serving both producers and the worker) and the <see cref="OutboxProcessor"/> background worker.
    /// Hosts that need durability register the PostgreSQL store afterwards, which replaces the in-memory one.
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
    /// Registers the vault-backed <see cref="IMessagingConfigStore"/> (masked read / merged write)
    /// used by admin and CLI editing surfaces. Requires an <c>IMutableSecretsProvider</c>.
    /// </summary>
    public static IServiceCollection AddStellaegisMessagingConfigStore(this IServiceCollection services)
    {
        services.TryAddScoped<IMessagingConfigStore, VaultMessagingConfigStore>();
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

    /// <summary>
    /// Registers system template infrastructure: <see cref="ISystemTemplateProvider"/>
    /// and <see cref="ITemplateEmailSender"/>.
    /// Reads configuration from the <c>Stellaegis:Messaging:Templates</c> section.
    /// </summary>
    public static IServiceCollection AddStellaegisSystemTemplates(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = SystemTemplateOptions.SectionName)
    {
        services.Configure<SystemTemplateOptions>(configuration.GetSection(sectionName));
        services.TryAddSingleton<ISystemTemplateProvider, SystemTemplateProvider>();
        services.TryAddTransient<ITemplateEmailSender, TemplateEmailSender>();
        return services;
    }
}
