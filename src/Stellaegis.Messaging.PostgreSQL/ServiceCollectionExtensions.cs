using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stellaegis.Database.ConnectionFactory;
using Stellaegis.Messaging.Outbox;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.PostgreSQL.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the durable PostgreSQL outbox store, replacing the in-memory store wired by
    /// <c>AddStellaegisEmail</c> (works whichever order the two are called). Requires
    /// <see cref="IDbConnectionFactory"/> (e.g. via <c>AddStellaegisPostgreSQL()</c>). The payload
    /// passphrase comes from configuration (<see cref="MessagingDatabaseOptions"/>), like the vault key.
    /// Reads options from <c>Stellaegis:Messaging:Database</c>.
    /// </summary>
    public static IServiceCollection AddStellaegisMessagingPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingDatabaseOptions.SectionName)
    {
        RegisterDapperTypeMaps();

        services.Configure<MessagingDatabaseOptions>(configuration.GetSection(sectionName));

        // Win over the in-memory outbox regardless of registration order.
        services.RemoveAll<IMessageOutbox>();
        services.RemoveAll<IMessageOutboxStore>();

        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            var config = sp.GetRequiredService<IConfiguration>();
            var options = sp.GetRequiredService<IOptions<MessagingDatabaseOptions>>().Value;

            var connectionString = options.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(options.ConnectionStringName))
                connectionString = config.GetConnectionString(options.ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Messaging outbox: no connection string configured (Stellaegis:Messaging:Database).");

            var connection = factory.CreateAsync(connectionString).GetAwaiter().GetResult();
            return new PostgreSqlMessageOutboxStore(connection, options);
        });

        services.AddScoped<IMessageOutboxStore>(sp => sp.GetRequiredService<PostgreSqlMessageOutboxStore>());
        services.AddScoped<IMessageOutbox>(sp => sp.GetRequiredService<IMessageOutboxStore>());

        return services;
    }

    // Forces store static constructors to run so their SqlMapper.SetTypeMap calls register, even if the
    // store is never instantiated directly.
    private static void RegisterDapperTypeMaps() =>
        RuntimeHelpers.RunClassConstructor(typeof(PostgreSqlMessageOutboxStore).TypeHandle);
}
