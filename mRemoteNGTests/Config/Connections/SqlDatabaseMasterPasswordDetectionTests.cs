using System;
using System.Runtime.Versioning;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// Whether the upgrade has to ask for a master password before it can run.
/// </summary>
/// <remarks>
/// <para>
/// The question exists so that a database with no master password does not put a password box in
/// front of somebody who has never had one to type — and so that one which does have a master
/// password is not upgraded on the default key, which would fail authentication anyway but with a
/// message about the wrong thing.
/// </para>
/// <para>
/// <b>It cannot be answered by looking at whether the column is populated.</b> <c>Protected</c> is
/// never empty in a real database: an unprotected store holds "ThisIsNotProtected" encrypted under
/// the built-in default key, not nothing at all. So the only way to know is to try the default key
/// and read what comes back.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlDatabaseMasterPasswordDetectionTests
{
    [Test]
    public void AStoreOnTheDefaultKeyNeedsNoPassword()
    {
        SqlConnectionListMetaData metaData = MetaData(
            Legacy().Encrypt(ConnectionFileDefaults.NotProtectedSentinel, DefaultKey()));

        Assert.That(SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData), Is.False);
    }

    [Test]
    public void AStoreWithAMasterPasswordNeedsOne()
    {
        SqlConnectionListMetaData metaData = MetaData(
            Legacy().Encrypt(ConnectionFileDefaults.ProtectedSentinel,
                             "the master password".ConvertToSecureString()));

        Assert.That(SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData));
    }

    [Test]
    public void ASentinelThatDecryptsToNonsenseNeedsOne()
    {
        // The legacy provider is AES-CBC with no authentication tag, so a wrong key does not fail
        // reliably — roughly one attempt in 256 yields valid padding and arbitrary bytes. Asking for
        // the password is the right answer either way: it is what a protected store looks like, and
        // for a corrupt one the password prompt is followed by a refusal that says so, rather than a
        // silent upgrade under a key nothing was encrypted with.
        SqlConnectionListMetaData metaData = MetaData(
            Legacy().Encrypt("not a sentinel at all", DefaultKey()));

        Assert.That(SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData));
    }

    [Test]
    public void AnEmptySentinelNeedsNoPassword()
    {
        // Not reachable from a database this application wrote, and answered rather than thrown:
        // Apply treats the same state as "no master password" and authenticates on the default key.
        // The two must agree, or the prompt would collect a password the upgrade then ignores.
        Assert.That(SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(MetaData("")), Is.False);
    }

    private static SqlConnectionListMetaData MetaData(string protectedSentinel) =>
        new()
        {
            Name = "test",
            Protected = protectedSentinel,
            ConfVersion = SqlDatabaseVersionVerifier.SchemaVersion
        };

    private static LegacyRijndaelCryptographyProvider Legacy() => new();

    private static System.Security.SecureString DefaultKey() =>
        new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();
}
