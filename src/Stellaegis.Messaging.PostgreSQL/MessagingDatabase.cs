using Stellaegis.Primitives.Data;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// What the PostgreSQL outbox declares about its database: the schema <c>msg</c>, its numbered scripts, the
/// pgcrypto extension. An installer applies it under the role that owns the schema
/// (<see cref="DatabaseLifecycle.InstallAsync"/>); the application verifies it at startup under its own role
/// (<see cref="DatabaseLifecycle.VerifyAsync"/>, registered by <c>AddStellaegisEmailPostgreSql</c>).
/// </summary>
/// <remarks>
/// No table under row-level security: the outbox is a buffer of the sending engine, whose single worker claims
/// the messages of every compartment on one connection, which a rule on the space would hide from it. The store
/// reaches the table on its own data source, outside any unit of work of <c>Stellaegis.Primitives.Data</c>.
/// </remarks>
public static class MessagingDatabase
{
    /// <summary>The schema holding the outbox, <c>msg</c>.</summary>
    public const string Schema = "msg";

    /// <summary>The declaration of the outbox's database.</summary>
    public static ModuleDatabase Value { get; } = ModuleDatabase.PostgreSql<MessagingModule>(Schema)
        .WithScripts(SqlScripts.FromResources(typeof(MessagingDatabase).Assembly, "Stellaegis.Messaging.PostgreSQL.Sql.PostgreSql"))
        .RequiringExtension("pgcrypto");
}
