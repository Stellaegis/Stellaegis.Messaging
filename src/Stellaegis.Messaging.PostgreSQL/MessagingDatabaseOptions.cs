namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Connection and at-rest encryption options for the PostgreSQL outbox store.
/// The payload passphrase follows the <b>same principle as the vault master key</b>
/// (<c>Stellaegis:Secrets</c>): it is provided by process configuration, never fetched from another
/// store — so the outbox never reaches outside for its own key.
/// </summary>
/// <remarks>
/// Key rotation uses the vault's modulo-2 double-buffer: two named slots (1 and 2) alternate as the
/// active slot; each <c>msg.outbox</c> row records the slot that encrypted it (<c>key_slot</c>), so both
/// keys stay valid during the overlap window without re-encrypting in-flight rows.
/// <para>
/// Configuration (rotation-ready):
/// <code>
/// Stellaegis:Messaging:Database:ConnectionStringName = messaging_db
/// Stellaegis:Messaging:Database:ActiveKeySlot        = 1
/// Stellaegis:Messaging:Database:Keys:1               = passphrase-for-slot-1
/// Stellaegis:Messaging:Database:Keys:2               = passphrase-for-slot-2  (empty until first rotation)
/// </code>
/// </para>
/// <para>
/// Legacy (single passphrase, treated as slot 1):
/// <code>
/// Stellaegis:Messaging:Database:Passphrase = passphrase
/// </code>
/// </para>
/// </remarks>
public sealed class MessagingDatabaseOptions
{
    /// <summary>Configuration section name in <c>appsettings</c>.</summary>
    public const string SectionName = "Stellaegis:Messaging:Database";

    /// <summary>
    /// Name of the connection string in the root <c>ConnectionStrings</c> section (e.g. "messaging_db").
    /// Preferred over <see cref="ConnectionString"/>.
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>Legacy inline connection string. Prefer <see cref="ConnectionStringName"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Named passphrases indexed by slot number (1 or 2).
    /// Maps to <c>Stellaegis:Messaging:Database:Keys:1</c> and <c>:Keys:2</c>.
    /// </summary>
    public Dictionary<int, string> Keys { get; set; } = [];

    /// <summary>Slot currently used to encrypt new rows (1 or 2). Switch it to activate a rotation.</summary>
    public int ActiveKeySlot { get; set; } = 1;

    /// <summary>
    /// Legacy single passphrase — treated as <c>Keys[1]</c> when <see cref="Keys"/> is empty.
    /// Source it from an environment variable / user secret, never hardcode it.
    /// </summary>
    public string Passphrase { get; set; } = "";

    /// <summary>
    /// Returns the passphrase for the given slot. The active slot throws when not configured (startup
    /// guard); a non-active slot returns empty when not configured — safe as long as no row uses it
    /// (the SQL <c>CASE</c> never calls <c>pgp_sym_decrypt</c> with an unused slot's value).
    /// </summary>
    internal string GetPassphrase(int slot)
    {
        if (Keys.TryGetValue(slot, out var key) && !string.IsNullOrWhiteSpace(key))
            return key;

        if (slot == 1 && !string.IsNullOrWhiteSpace(Passphrase))
            return Passphrase;

        if (slot != ActiveKeySlot)
            return string.Empty;

        throw new InvalidOperationException(
            $"No passphrase configured for active key slot {slot}. " +
            $"Set Stellaegis:Messaging:Database:Keys:{slot} or Stellaegis:Messaging:Database:Passphrase.");
    }

    /// <summary>Passphrase for the currently active slot — used to encrypt all new rows.</summary>
    internal string ActivePassphrase => GetPassphrase(ActiveKeySlot);
}
