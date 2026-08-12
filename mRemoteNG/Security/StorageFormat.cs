using System;

namespace mRemoteNG.Security;

/// <summary>
/// How a connection store is written, and therefore what else can read it.
/// </summary>
public enum StorageFormatLevel
{
    /// <summary>
    /// The format upstream mRemoteNG understands. The default, permanently.
    /// </summary>
    Classic,

    /// <summary>
    /// This fork's hardened format. Nothing else can read it.
    /// </summary>
    Hardened
}

/// <summary>
/// Resolves a store's format level, for the connection file and the SQL database alike.
/// </summary>
/// <remarks>
/// <para>
/// This fork writes <c>%APPDATA%\mRemoteNG\confCons.xml</c> — upstream mRemoteNG's own directory and
/// filename — so a user who installs both, or who tries this fork and goes back, has two
/// applications reading one file. Security changes that alter the format would take away their
/// ability to leave, silently, as a side effect of an ordinary save.
/// </para>
/// <para>
/// So a store carries a level, classic is the default, and nothing raises it without being asked.
/// Every hardening change gates on this rather than applying on next save.
/// </para>
/// </remarks>
public static class StorageFormat
{
    /// <summary>
    /// The root attribute recording the level on a connection file.
    /// </summary>
    /// <remarks>
    /// Written only when the level is hardened. A classic file must come out byte-compatible with
    /// what upstream writes, so absence is what means classic — the same rule
    /// <c>harden-connection-file-kdf</c> applies to the key derivation parameters.
    /// </remarks>
    public const string AttributeName = "StorageFormat";

    private const string HardenedValue = "Hardened";
    private const string ClassicValue = "Classic";

    /// <summary>
    /// The SQL schema version at which the store uses authenticated encryption.
    /// </summary>
    /// <remarks>
    /// Reserved here so both stores describe their level the same way; nothing writes it until
    /// <c>encrypt-sql-backend-with-aead</c> lands. Until then every SQL database resolves to classic,
    /// which is correct — none of them are hardened yet.
    /// </remarks>
    public static readonly Version SqlHardenedVersion = new(3, 6);

    /// <summary>
    /// Reads the level from a connection file's recorded value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absent, empty and whitespace are classic: that is every file written before the level existed
    /// and every file upstream mRemoteNG writes. A value this build does not recognise resolves to
    /// nothing at all — see <see cref="Resolve"/> — because it says a build that knew more than this
    /// one wrote the file deliberately, and reading it as classic discards that statement.
    /// </para>
    /// <para>
    /// An earlier version of this collapsed unrecognised into classic, on the grounds that such a
    /// file is refused by the protection sentinel before anything is decrypted. That is true of the
    /// SQL store, where <c>SqlConnectionsLoader</c> sets
    /// <c>PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel</c>. It was never true of the
    /// connection file, which builds its authenticator through <c>XmlConnectionsDecryptor</c> and
    /// sets no validator — so <c>PasswordAuthenticator</c> accepts any plaintext that decrypts
    /// without throwing, and nothing downstream would have caught the unknown level.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The level, or <see langword="null"/> when the value is present but unrecognised. A caller that
    /// cannot resolve a level must refuse the store rather than assume one.
    /// </returns>
    public static StorageFormatLevel? Resolve(string? recordedValue)
    {
        if (string.IsNullOrWhiteSpace(recordedValue))
            return StorageFormatLevel.Classic;

        if (string.Equals(recordedValue, HardenedValue, StringComparison.OrdinalIgnoreCase))
            return StorageFormatLevel.Hardened;

        return string.Equals(recordedValue, ClassicValue, StringComparison.OrdinalIgnoreCase)
            ? StorageFormatLevel.Classic
            : null;
    }

    /// <summary>
    /// Whether a recorded value names a level this build understands.
    /// </summary>
    /// <remarks>
    /// Absence counts as recognised: it is the classic declaration, not a missing one.
    /// </remarks>
    public static bool IsRecognised(string? recordedValue) => Resolve(recordedValue) is not null;

    /// <summary>The value to record for a level, or null when nothing should be written.</summary>
    public static string? ToRecordedValue(StorageFormatLevel level) =>
        level == StorageFormatLevel.Hardened ? HardenedValue : null;

    /// <summary>The level of a SQL database, from the schema version it records.</summary>
    public static StorageFormatLevel ForSqlDatabase(Version? confVersion) =>
        confVersion is not null && confVersion.CompareTo(SqlHardenedVersion) >= 0
            ? StorageFormatLevel.Hardened
            : StorageFormatLevel.Classic;

    /// <summary>A name for the level, for messages and diagnostics.</summary>
    public static string Describe(StorageFormatLevel level) =>
        level == StorageFormatLevel.Hardened ? HardenedValue : ClassicValue;
}
