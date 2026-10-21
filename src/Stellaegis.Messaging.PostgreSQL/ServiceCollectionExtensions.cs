using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Stellaegis.Messaging.Outbox;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Dependency injection helpers for Stellaegis.Messaging.PostgreSQL.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email engine with the durable PostgreSQL outbox, in place of <c>AddStellaegisEmail</c>: a host
    /// calls one of the two, once. Reads the connection string and the payload passphrases from
    /// <c>Stellaegis:Messaging:Database</c> (<see cref="MessagingDatabaseOptions"/>), validated when the host
    /// starts, and declares the outbox's database (<see cref="MessagingDatabase"/>) for its verification at
    /// startup, which <c>AddStellaegisDatabaseVerification()</c> of <c>Stellaegis.Primitives.Data</c> registers.
    /// </summary>
    /// <remarks>
    /// The <c>msg</c> schema is installed by <c>DatabaseLifecycle.InstallAsync([MessagingDatabase.Value], …)</c>,
    /// under a role that owns it; the application connects under the role of its connection string.
    /// </remarks>
    public static IServiceCollection AddStellaegisEmailPostgreSql(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = MessagingDatabaseOptions.SectionName)
    {
        Guard.NotNull(services);
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);

        services.AddStellaegisOptions<MessagingDatabaseOptions>(configuration, sectionName);
        services.TryAddSingleton<IMessageOutboxStore, PostgreSqlMessageOutboxStore>();
        services.AddStellaegisModuleDatabase(MessagingDatabase.Value, OpenAsApplicationAsync);
        return services.AddStellaegisEmailServices(configuration);
    }

    // The verification opens the outbox as the store does: under the connection string of the options.
    private static async Task<DbConnection> OpenAsApplicationAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(services.GetRequiredService<IOptions<MessagingDatabaseOptions>>().Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
