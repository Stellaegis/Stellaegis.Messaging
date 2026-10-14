using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Messaging.Outbox;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.PostgreSQL.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the durable PostgreSQL outbox store, replacing the in-memory store wired by
    /// <c>AddStellaegisEmail</c> whichever order the two are called in. Reads the connection string and
    /// the payload passphrase from <c>Stellaegis:Messaging:Database</c> (<see cref="MessagingDatabaseOptions"/>).
    /// The <c>msg</c> schema is created by <c>docs/sql/stellaegis_messaging.sql</c> of the repository.
    /// </summary>
    public static IServiceCollection AddStellaegisMessagingPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingDatabaseOptions.SectionName)
    {
        services.Configure<MessagingDatabaseOptions>(configuration.GetSection(sectionName));

        // Win over the in-memory outbox regardless of registration order: AddStellaegisEmail only
        // TryAdds its own.
        services.RemoveAll<IMessageOutbox>();
        services.RemoveAll<IMessageOutboxStore>();

        services.TryAddSingleton<PostgreSqlMessageOutboxStore>();
        services.AddSingleton<IMessageOutboxStore>(sp => sp.GetRequiredService<PostgreSqlMessageOutboxStore>());
        services.AddSingleton<IMessageOutbox>(sp => sp.GetRequiredService<IMessageOutboxStore>());

        return services;
    }
}
