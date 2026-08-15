using System;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Factories;

/// <summary>
/// Which provider protects a SQL database's secrets, decided from the version it records.
/// </summary>
/// <remarks>
/// <para>
/// The SQL side had no equivalent of <c>CryptoProviderFactoryFromXml</c>: the saver and the loader
/// each constructed the legacy provider outright, so the format was whatever the code said rather
/// than what the database said. These tests pin the rule that replaces that, and — more importantly
/// — that the two ends agree on it. A saver and a loader that disagree write a database nothing can
/// read back.
/// </para>
/// <para>
/// The gate is the version, never the ciphertext. Sniffing would decide per value, which makes a
/// half-migrated table readable, and half-migrated is the state most worth making impossible: it
/// means an interrupted upgrade left some rows recoverable at GPU speed and nothing to say which.
/// </para>
/// </remarks>
[TestFixture]
public class CryptoProviderFactoryFromSqlVersionTests
{
    [TestCase(2, 2)]
    [TestCase(3, 4)]
    [TestCase(3, 5)]
    public void BelowTheAuthenticatedVersionTheDatabaseIsLegacy(int major, int minor)
    {
        Assert.That(CryptoProviderFactoryFromSqlVersion.ProviderFor(new Version(major, minor)),
            Is.TypeOf<LegacyRijndaelCryptographyProvider>());
    }

    [TestCase(3, 6)]
    [TestCase(3, 7)]
    [TestCase(4, 0)]
    public void AtOrAboveTheAuthenticatedVersionTheDatabaseIsAead(int major, int minor)
    {
        Assert.That(CryptoProviderFactoryFromSqlVersion.ProviderFor(new Version(major, minor)),
            Is.TypeOf<AeadCryptographyProvider>());
    }

    [Test]
    public void AnUnknownVersionIsTreatedAsLegacy()
    {
        // Null means the metadata row could not be read — a brand-new database this save is about
        // to create, or one this build does not understand. Legacy is the safe direction and the
        // asymmetry is the reason: reading legacy ciphertext with the AEAD provider fails cleanly,
        // because GCM authenticates. Reading AEAD ciphertext with the legacy provider does not
        // fail at all — AES-CBC has no tag, so it yields plausible nonsense and the user sees
        // connections with empty passwords, which reads as data loss rather than a version problem.
        Assert.That(CryptoProviderFactoryFromSqlVersion.ProviderFor(null),
            Is.TypeOf<LegacyRijndaelCryptographyProvider>());
    }

    [Test]
    public void TheSchemaVersionAndTheAuthenticatedVersionDoNotOverlap()
    {
        // Every database in the field is at the schema version and is legacy; nothing upgrades one
        // except a deliberate act. If these two ever became the same value, every existing database
        // would appear already upgraded and would be read with a provider its contents were never
        // written by.
        Assert.Multiple(() =>
        {
            Assert.That(CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(
                SqlDatabaseVersionVerifier.SchemaVersion), Is.False);
            Assert.That(CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(
                CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));
        });
    }

    [Test]
    public void TheFactoryAndTheConvenienceAgree()
    {
        Version version = CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion;

        ICryptographyProvider built = new CryptoProviderFactoryFromSqlVersion(version).Build();

        Assert.That(built.GetType(),
            Is.EqualTo(CryptoProviderFactoryFromSqlVersion.ProviderFor(version).GetType()));
    }

    [Test]
    public void ANewDatabaseIsCreatedAtAVersionThisBuildCanWriteTo()
    {
        // The trap §3 introduced and this catches. The saver refuses to write a database that still
        // stores its secrets weakly, so a database created at the older version would be readable
        // and permanently unwritable — broken on its second save, by the build that made it.
        Assert.That(CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(
            CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion),
            "a database created at this version must be one the saver will accept");
    }

    [Test]
    public void TheRefusalTellsTheUserWhatToDoAndWhatIsSafe()
    {
        // A refused save is the one place in this change a user is stopped, and the message is most
        // of its value: it has to name the remedy, and it has to say the connections already in the
        // database are unharmed — which is the first thing anyone will ask.
        string message = mRemoteNG.Resources.Language.Language.ErrorDatabaseNotUpgradedForEncryption;

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("Nothing was saved"),
                "the user must not believe their change was stored");
            Assert.That(message, Does.Contain("SQL Server"), "and must be told where to go");
            Assert.That(message, Does.Contain("unchanged"), "and that nothing was harmed");
        });
    }

    [Test]
    public void EachCallGetsItsOwnProvider()
    {
        // The AEAD provider caches derived keys and salts in fields, so handing the same instance to
        // a saver and a loader — or across the threads a batch decrypt uses — would produce
        // intermittent wrong answers rather than a clean failure. `XmlConnectionsDecryptor` refuses
        // a shared provider at construction for exactly this reason.
        Version version = CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion;

        Assert.That(CryptoProviderFactoryFromSqlVersion.ProviderFor(version),
            Is.Not.SameAs(CryptoProviderFactoryFromSqlVersion.ProviderFor(version)));
    }
}
