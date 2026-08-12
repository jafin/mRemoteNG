namespace mRemoteNG.Security;

/// <summary>
/// Legacy default values for the connection file format.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="LegacyEncryptionKey"/> is baked into the confCons.xml file
/// format since the earliest versions of mRemoteNG. When a user has NOT set a
/// custom master password, this key is used to encrypt/decrypt connection
/// properties in the XML file.
/// </para>
/// <para>
/// BACKWARD COMPATIBILITY: changing this value would make every existing
/// connection file unreadable unless a migration path is implemented.
/// The XML serializer writes "ThisIsNotProtected" (vs "ThisIsProtected") as a
/// sentinel to distinguish files using this default key from those with a
/// user-chosen password.
/// </para>
/// </remarks>
public static class ConnectionFileDefaults
{
    /// <summary>
    /// The legacy default encryption key used when no master password is set.
    /// </summary>
    public const string LegacyEncryptionKey = "mR3m";

    /// <summary>
    /// Sentinel written when the store is protected by a user-chosen password.
    /// </summary>
    public const string ProtectedSentinel = "ThisIsProtected";

    /// <summary>
    /// Sentinel written when the store uses <see cref="LegacyEncryptionKey"/>.
    /// </summary>
    public const string NotProtectedSentinel = "ThisIsNotProtected";

    /// <summary>
    /// Sentinel written when the store is protected by a random per-file key, wrapped both by DPAPI
    /// and by a recovery password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A third value on the sentinel rather than a new attribute beside it. The sentinel is already
    /// read first and already discriminates protection; a parallel attribute would give two sources
    /// of truth about the same question, and the first disagreement between them would be a file
    /// that decrypts under one reading and not the other.
    /// </para>
    /// <para>
    /// Written only by the XML connection file. A per-user wrapped key is meaningless for a SQL
    /// store several people read, so that backend stays on the two values above — see
    /// <c>SqlConnectionsLoader</c> and the <c>require-sql-master-password</c> change.
    /// </para>
    /// </remarks>
    public const string PerFileKeySentinel = "ThisIsDpapiProtected";

    /// <summary>
    /// Whether decrypting the stored sentinel produced one of the values the format defines.
    /// </summary>
    /// <remarks>
    /// The legacy provider is AES-CBC with PKCS7 and no authentication tag, so decrypting with the
    /// wrong key yields valid padding often enough to matter — roughly one attempt in 256 — and
    /// returns arbitrary bytes rather than failing. A decryption that merely completed is therefore
    /// not evidence of the key; only the plaintext is.
    /// </remarks>
    public static bool IsKnownSentinel(string? plainText) =>
        string.Equals(plainText, ProtectedSentinel, System.StringComparison.Ordinal) ||
        string.Equals(plainText, NotProtectedSentinel, System.StringComparison.Ordinal) ||
        string.Equals(plainText, PerFileKeySentinel, System.StringComparison.Ordinal);
}