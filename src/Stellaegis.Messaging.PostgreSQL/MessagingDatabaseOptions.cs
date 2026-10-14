namespace Stellaegis.Messaging.PostgreSQL;

/// <summary>
/// Connection and at-rest encryption options for the PostgreSQL outbox store.
/// The payload passphrase follows the same principle as the master key of a vault: it is provided by
/// process configuration, never fetched from another store — so the outbox never reaches outside for
/// its own key.
/// </summary>
/// <remarks>
/// The simple form holds one passphrase:
/// <code>
/// Stellaegis:Messaging:Database:ConnectionString = Host=...;Database=...
/// Stellaegis:Messaging:Database:Passphrase       = passphrase
/// </code>
/// The form that rotates holds two slots. New rows are encrypted with the active slot; each row records
/// the slot that encrypted it (<c>key_slot</c>), so both passphrases stay valid while they overlap:
/// <code>
/// Stellaegis:Messaging:Database:ActiveKeySlot = 1
/// Stellaegis:Messaging:Database:Keys:1        = passphrase-for-slot-1
/// Stellaegis:Messaging:Database:Keys:2        = passphrase-for-slot-2  (empty until the first rotation)
/// </code>
/// Source passphrases from environment variables or user secrets, never from a committed file.
/// </remarks>
public sealed class MessagingDatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stellaegis:Messaging:Database";

    /// <summary>
    /// Connection string of the database holding the <c>msg</c> schema. To set it from code — a
    /// connection string kept elsewhere, for instance — use
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

    /// <summary>
    /// Single passphrase, the simple form: used as slot 1 when <see cref="Keys"/> has no slot 1.
    /// </summary>
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>
    /// Returns the passphrase for the given slot. The active slot throws when not configured (startup
    /// guard); a non-active slot returns empty when not configured — safe as long as no row uses it
    /// (the SQL <c>case</c> never calls <c>pgp_sym_decrypt</c> with an unused slot's value).
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
            $"Set {SectionName}:Keys:{slot} or {SectionName}:Passphrase.");
    }

    /// <summary>Passphrase of the active slot — used to encrypt all new rows.</summary>
    internal string ActivePassphrase => GetPassphrase(ActiveKeySlot);
}
