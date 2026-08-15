using System;
using System.Runtime.Versioning;
using mRemoteNG.Security.SymmetricEncryption;

namespace mRemoteNG.Security.Factories;

/// <summary>
/// Chooses how a SQL database's secrets are encrypted, from the version the database records.
/// </summary>
/// <remarks>
/// <para>
/// The SQL side had no equivalent of <see cref="CryptoProviderFactoryFromXml"/> because nothing in
/// the schema described the encryption — the saver and the loader each constructed
/// <see cref="LegacyRijndaelCryptographyProvider"/> outright, so the format was whatever the code
/// happened to say that day. The schema does carry a version, and that is what this reads.
/// </para>
/// <para>
/// <b>The gate is the version, not the ciphertext.</b> Sniffing would be possible — GCM output
/// carries a salt and nonce the legacy format does not — but it decides per value, which makes a
/// half-migrated table readable. Half-migrated is the state most worth making impossible: it means
/// an interrupted upgrade left some rows recoverable at GPU speed and nothing to say which. A
/// version gate makes the database atomically one thing or the other, and the re-encryption happens
/// in the transaction that raises the version.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class CryptoProviderFactoryFromSqlVersion : ICryptoProviderFactory
{
    /// <summary>
    /// The first database version whose secrets are protected by authenticated encryption.
    /// </summary>
    /// <remarks>
    /// 3.5 and 3.6 share a schema exactly. Nothing about the tables differs, so there is no upgrader
    /// for this step and there deliberately is not one: the change is to the contents of the secret
    /// columns, it re-encrypts every row, and it locks out every client still on an older build —
    /// including upstream mRemoteNG, which reaches the same databases. That is an administrative
    /// decision, not something the version-upgrade chain should perform because a database happened
    /// to be opened.
    /// </remarks>
    public static readonly Version AuthenticatedEncryptionVersion = new(3, 6);

    private readonly Version? _databaseVersion;

    public CryptoProviderFactoryFromSqlVersion(Version? databaseVersion)
    {
        _databaseVersion = databaseVersion;
    }

    /// <summary>
    /// Whether a database at this version holds authenticated ciphertext.
    /// </summary>
    /// <remarks>
    /// A null version is treated as legacy. It means the metadata row could not be read at all,
    /// which is either a brand-new database this save is about to initialise or one this build
    /// cannot understand — and in neither case is guessing at AEAD the safe direction: reading
    /// legacy ciphertext with the AEAD provider fails cleanly, while the reverse produces plausible
    /// nonsense out of an unauthenticated decrypt.
    /// </remarks>
    public static bool UsesAuthenticatedEncryption(Version? databaseVersion) =>
        databaseVersion is not null && databaseVersion >= AuthenticatedEncryptionVersion;

    public ICryptographyProvider Build() =>
        UsesAuthenticatedEncryption(_databaseVersion)
            ? new AeadCryptographyProvider()
            : new LegacyRijndaelCryptographyProvider();

    /// <summary>The provider for a given database version. Convenience over constructing the factory.</summary>
    public static ICryptographyProvider ProviderFor(Version? databaseVersion) =>
        new CryptoProviderFactoryFromSqlVersion(databaseVersion).Build();
}
