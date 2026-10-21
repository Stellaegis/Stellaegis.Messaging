using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Connection and at-rest encryption options for the PostgreSQL outbox store.
/// The payload passphrase follows the same principle as the master key of a vault: it is provided by
/// process configuration, never fetched from another store — so the outbox never reaches outside for
/// its own key.
/// </summary>
/// <remarks>
/// The passphrases rotate by slots, the shape of <see cref="KeySlots"/>: two slots, 1 and 2, one active for new
/// rows, each row recording the slot that encrypted it (<c>key_slot</c>), so both passphrases stay valid while
/// they overlap.
/// <code>
/// Stellaegis:Messaging:Database:ConnectionString = Host=...;Database=...
/// Stellaegis:Messaging:Database:ActiveKeySlot    = 1
/// Stellaegis:Messaging:Database:Keys:1           = passphrase-for-slot-1
/// Stellaegis:Messaging:Database:Keys:2           = passphrase-for-slot-2  (empty until the first rotation)
/// </code>
/// Source passphrases from environment variables or user secrets, never from a committed file. Validated when the
/// host starts: a missing connection string or an active slot without a passphrase stops the start, naming the
/// setting.
/// </remarks>
public sealed class MessagingDatabaseOptions : IValidatableOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging:Database";

    /// <summary>
    /// Connection string of the database holding the <c>msg</c> schema, under the application's role. To set it
    /// from code — a connection string kept elsewhere, for instance — use
    /// <c>services.PostConfigure&lt;MessagingDatabaseOptions&gt;(o =&gt; ...)</c>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Passphrases indexed by slot number (1 or 2), for rotation.
    /// Maps to <c>Stellaegis:Messaging:Database:Keys:1</c> and <c>:Keys:2</c>.
    /// </summary>
    public Dictionary<int, string> Keys { get; set; } = [];

    /// <summary>Slot used to encrypt new rows (1 or 2). Switch it to activate a rotation.</summary>
    public int ActiveKeySlot { get; set; } = 1;

    /// <summary>The passphrases as slots, the shape every rotating secret of the family shares.</summary>
    internal KeySlots Slots => new() { Keys = Keys, ActiveKeySlot = ActiveKeySlot };

    /// <summary>
    /// The passphrase of <paramref name="slot"/>: the active slot always has one; the other may be empty as long as
    /// no row uses it — the SQL <c>case</c> never calls <c>pgp_sym_decrypt</c> with an unused slot's value.
    /// </summary>
    internal string GetPassphrase(int slot) => Slots.KeyOf(slot);

    /// <summary>Passphrase of the active slot — used to encrypt all new rows.</summary>
    internal string ActivePassphrase => Slots.ActiveKey;

    /// <inheritdoc />
    public string? FindError()
        => string.IsNullOrWhiteSpace(ConnectionString) ? "ConnectionString is required." : Slots.FindError();
}
