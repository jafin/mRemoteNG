using System;
using System.Runtime.Versioning;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sql;

/// <summary>
/// What version a real database ends up recording, and which provider that selects.
/// </summary>
/// <remarks>
/// <para>
/// The version marker is the whole gate for SQL encryption: it decides which provider reads the
/// rows and which writes them, and a database whose marker and contents disagree is the one state
/// nothing recovers from. Until this fixture existed, every claim about that marker was made against
/// substitutes — including the claim that turned out to be false, that a save preserved it.
/// </para>
/// <para>
/// So these run against a real engine: real schema initialisation, the real metadata row, the real
/// <c>INSERT</c>. That is the only way to catch a defect that lives in what the database ends up
/// holding rather than in what the code appears to say.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlDatabaseVersionIntegrationTests
{
    private MSSqlDatabaseConnector _connector = null!;
    private readonly SqlDatabaseMetaDataRetriever _retriever = new();

    [SetUp]
    public void Setup()
    {
        if (SqlServerFixture.Skipped)
            Assert.Ignore($"{SqlServerFixture.SkipVariable}=1");

        _connector = SqlServerFixture.CreateDatabase(
            "mrng_" + TestContext.CurrentContext.Test.ID.Replace("-", "", StringComparison.Ordinal));
        _connector.Connect();
    }

    [TearDown]
    public void Teardown() => _connector?.Dispose();

    [Test]
    public void AnEmptyDatabaseGetsItsSchemaAndReportsNoMetadataYet()
    {
        // The first-run path: tblRoot does not exist, so the schema is created, and the absence of a
        // metadata row is reported as null rather than invented. The saver reads that null as "this
        // save is creating the database".
        SqlConnectionListMetaData? metaData = _retriever.GetDatabaseMetaData(_connector);

        Assert.That(metaData, Is.Null);
    }

    [Test]
    public void ANewDatabaseIsCreatedAtTheAuthenticatedVersion()
    {
        // The §2/§3 decision, end to end. A database created at the older version would be one this
        // build could read and — while the refusal stood — never write to again. The refusal is now
        // a warning, but the reasoning survives: nothing reads a database this build has only just
        // created, so there is nobody to stay compatible with and no reason to start it weak.
        // With a master password, because that is what a database at this version is keyed on and
        // what any real save carries. An unprotected root is refused here rather than recorded, and
        // `SqlMasterPasswordRequirementTests` is where that refusal is asserted.
        RootNodeInfo root = new(RootNodeType.Connection) { PasswordString = "the master password" };

        _retriever.GetDatabaseMetaData(_connector);
        _retriever.WriteDatabaseMetaData(root, _connector, null);

        SqlConnectionListMetaData written = _retriever.GetDatabaseMetaData(_connector)!;

        Assert.Multiple(() =>
        {
            Assert.That(written.ConfVersion,
                Is.EqualTo(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));
            Assert.That(CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(written.ConfVersion));
        });
    }

    [Test]
    public void WritingMetadataPreservesTheVersionTheDatabaseAlreadyHas()
    {
        // **The defect this fixture exists to have caught.** WriteDatabaseMetaData used to stamp
        // ConnectionsFileInfo.ConnectionFileVersion — the XML file-format constant, 3.2 — into
        // tblRoot on every save, whatever the database actually was. Harmless churn while nothing
        // depended on the number; fatal once it decides how secrets are encrypted, because an
        // upgraded database would be marked legacy by the first ordinary save while its rows were
        // written as AEAD. Nothing recovers from that, and nobody would have done anything wrong.
        _retriever.GetDatabaseMetaData(_connector);
        RootNodeInfo root = new(RootNodeType.Connection);

        _retriever.WriteDatabaseMetaData(root, _connector, null, SqlDatabaseVersionVerifier.SchemaVersion);

        SqlConnectionListMetaData afterFirstSave = _retriever.GetDatabaseMetaData(_connector)!;
        Assert.That(afterFirstSave.ConfVersion, Is.EqualTo(SqlDatabaseVersionVerifier.SchemaVersion),
            "a legacy database stays legacy");

        // And again, feeding back what was read — which is what the saver does on every save.
        _retriever.WriteDatabaseMetaData(root, _connector, null, afterFirstSave.ConfVersion);

        Assert.That(_retriever.GetDatabaseMetaData(_connector)!.ConfVersion,
            Is.EqualTo(SqlDatabaseVersionVerifier.SchemaVersion),
            "and repeated saves do not walk it anywhere");
    }

    [Test]
    public void BothReadableVersionsAreAccepted()
    {
        // 3.5 and 3.6 share a schema and differ only in how the secret columns are encrypted, so a
        // real database at either must load. An equality check against the highest version — which
        // is what this was before — would have reported every database in the field as unsupported.
        _retriever.GetDatabaseMetaData(_connector);
        SqlDatabaseVersionVerifier verifier = new(_connector);

        Assert.Multiple(() =>
        {
            Assert.That(verifier.VerifyDatabaseVersion(SqlDatabaseVersionVerifier.SchemaVersion));
            Assert.That(verifier.VerifyDatabaseVersion(SqlDatabaseVersionVerifier.HighestSupportedVersion));
            Assert.That(verifier.IsNewerThanSupported(SqlDatabaseVersionVerifier.HighestSupportedVersion),
                Is.False);
        });
    }

    [Test]
    public void ADatabaseNewerThanThisBuildIsRefused()
    {
        // §1, against a real database rather than a substitute. What an un-upgraded client actually
        // does with one was measured for task 6.6, and it is not what this comment used to claim:
        // it never reaches the rows at all. The `Protected` sentinel is AEAD ciphertext too, so the
        // legacy provider fails on it first, and the client asks for a master password the database
        // does not have — then reports "Could not load SQL connections" with nothing in the
        // notification panel naming a version. Refusing by version is what turns that into a message
        // somebody can act on.
        _retriever.GetDatabaseMetaData(_connector);
        SqlDatabaseVersionVerifier verifier = new(_connector);

        Version fromTheFuture = new(SqlDatabaseVersionVerifier.HighestSupportedVersion.Major,
                                    SqlDatabaseVersionVerifier.HighestSupportedVersion.Minor + 1);

        Assert.Multiple(() =>
        {
            Assert.That(verifier.IsNewerThanSupported(fromTheFuture));
            Assert.That(verifier.VerifyDatabaseVersion(fromTheFuture), Is.False);
        });
    }
}
