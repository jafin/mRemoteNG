using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Config;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sql;

/// <summary>
/// What the saver actually writes into a database's secret columns, at each version.
/// </summary>
/// <remarks>
/// <para>
/// This is task 3.3, and it is the only test in the change that can answer the question the change
/// is about: not "which provider does the factory return" — that is a unit test — but "what is in
/// the column afterwards". A saver and a loader that disagree produce a database nothing can read
/// back, and no amount of testing either end alone would show it.
/// </para>
/// <para>
/// <b>These drive the saver through the application's SQL settings, because there is no seam.</b>
/// <c>SqlConnectionsSaver</c> builds its own connector from <c>DatabaseConnectorFromSettings()</c>,
/// so pointing it at the container means setting those settings and putting them back afterwards.
/// That is worth stating plainly rather than hiding in a helper: it is the reason this fixture
/// saves and restores five settings, and it is a seam worth adding if this file grows.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlSaverEncryptionIntegrationTests
{
    private const string ConnectionPassword = "hunter2";

    /// <summary>
    /// What a database at the authenticated-encryption version is keyed on.
    /// </summary>
    /// <remarks>
    /// There is no unprotected state at that version: a store keyed on the built-in default is
    /// readable by anyone who can read the table, so both the seeding and the saving below carry a
    /// master password once the database is at it. Below that version the default key is still what
    /// a store with no master password uses, and these tests still exercise that.
    /// </remarks>
    private const string MasterPassword = "the database master password";

    private MSSqlDatabaseConnector _connector = null!;
    private readonly SqlDatabaseMetaDataRetriever _retriever = new();

    private string _originalType = "";
    private string _originalHost = "";
    private string _originalCatalog = "";
    private string _originalUser = "";
    private string _originalPass = "";
    private string _originalAuthType = "";
    private bool _originalReadOnly;
    private string _catalog = "";

    /// <summary>The key the seeded database is on, which is what the save has to be given too.</summary>
    private string _storeKey = "";

    [SetUp]
    public void Setup()
    {
        if (SqlServerFixture.Skipped)
            Assert.Ignore($"{SqlServerFixture.SkipVariable}=1");

        _catalog = "mrng_" + TestContext.CurrentContext.Test.ID.Replace("-", "", StringComparison.Ordinal);
        _connector = SqlServerFixture.CreateDatabase(_catalog);
        _connector.Connect();

        mRemoteNG.Properties.OptionsDBsPage settings = mRemoteNG.Properties.OptionsDBsPage.Default;
        _originalType = settings.SQLServerType;
        _originalHost = settings.SQLHost;
        _originalCatalog = settings.SQLDatabaseName;
        _originalUser = settings.SQLUser;
        _originalPass = settings.SQLPass;
        _originalAuthType = settings.SQLAuthType;
        _originalReadOnly = settings.SQLReadOnly;

        settings.SQLServerType = DatabaseConnectorFactory.MsSqlType;
        settings.SQLHost = $"{SqlServerFixture.Host}:{SqlServerFixture.Port}";
        settings.SQLDatabaseName = _catalog;
        settings.SQLUser = SqlServerFixture.Username;

        // Set explicitly, and the tests do not work without it. Left at whatever the process
        // happened to hold, "Windows Authentication" makes the factory discard the username and
        // password and build an integrated-security connection string — whose DataSource is the
        // raw host setting, colon and port and all. SqlClient cannot parse that as host,port, falls
        // back to named pipes, and fails after thirty seconds with "the server was not found",
        // which describes a container that is running perfectly well.
        settings.SQLAuthType = "SQL Server Authentication";

        // Stored the way the application stores it. An unmarked value is *not* passed through:
        // `SettingsSecretProtector`'s no-marker fallback legacy-*decrypts*, because that is how it
        // reads settings written before it existed — so a plain password there fails as
        // "not a valid Base-64 string" from inside the save, which is a long way from anything
        // resembling the real fault.
        settings.SQLPass = SettingsSecretProtector.Default.Protect(
            SqlServerFixture.Password, Runtime.EncryptionKey);
        settings.SQLReadOnly = false;

        Runtime.MessageCollector.ClearMessages();
    }

    [TearDown]
    public void Teardown()
    {
        mRemoteNG.Properties.OptionsDBsPage settings = mRemoteNG.Properties.OptionsDBsPage.Default;
        settings.SQLServerType = _originalType;
        settings.SQLHost = _originalHost;
        settings.SQLDatabaseName = _originalCatalog;
        settings.SQLUser = _originalUser;
        settings.SQLPass = _originalPass;
        settings.SQLAuthType = _originalAuthType;
        settings.SQLReadOnly = _originalReadOnly;

        _connector?.Dispose();
    }

    [Test]
    public void ALegacyDatabaseIsStillWrittenAndItsSecretsStayLegacy()
    {
        // The first half of 3.3, restated after the refusal became a warning. It used to read "a
        // save against a legacy database does not write"; it now writes, because refusing improved
        // nothing and stopped people working. What must not happen is a legacy database receiving
        // AEAD ciphertext — a marker and contents that disagree is what nothing recovers from.
        SeedDatabaseAt(SqlDatabaseVersionVerifier.SchemaVersion);

        SaveOneConnection();

        Assert.Multiple(() =>
        {
            Assert.That(DecryptStoredPasswordWith(new LegacyRijndaelCryptographyProvider()),
                Is.EqualTo(ConnectionPassword), "written in the format the database declares");
            Assert.That(RecordedVersion(), Is.EqualTo(SqlDatabaseVersionVerifier.SchemaVersion),
                "and the save did not move the database anywhere");
        });
    }

    [Test]
    public void AnUpgradedDatabaseGetsAuthenticatedCiphertext()
    {
        // The second half, and the point of the whole change: at the new version the same save puts
        // AES-256-GCM in the column instead of unsalted-MD5-keyed AES-CBC.
        SeedDatabaseAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion);

        SaveOneConnection();

        Assert.Multiple(() =>
        {
            Assert.That(DecryptStoredPasswordWith(new AeadCryptographyProvider()),
                Is.EqualTo(ConnectionPassword));
            Assert.That(RecordedVersion(),
                Is.EqualTo(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));
        });
    }

    [Test]
    public void TheTwoFormatsAreNotInterchangeable()
    {
        // Proves the previous two tests assert something. If either provider could read the other's
        // output, "it decrypts with the legacy provider" would say nothing about what was written.
        SeedDatabaseAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion);
        SaveOneConnection();
        string aeadCiphertext = StoredPasswordCiphertext();

        Assert.That(() => new LegacyRijndaelCryptographyProvider().Decrypt(aeadCiphertext, MasterKey()),
            Throws.Exception,
            "the legacy provider must not quietly produce something from AEAD ciphertext");
    }

    [Test]
    public void ALegacyDatabaseIsWarnedAboutOnceAndNotOnEverySave()
    {
        // A save runs on a debounce timer, so a warning per save would arrive during ordinary typing
        // and be trained away within a day. Said once per database, it is still there to be found in
        // the notification panel when somebody goes looking.
        SeedDatabaseAt(SqlDatabaseVersionVerifier.SchemaVersion);

        SaveOneConnection();
        int afterFirst = WeakEncryptionWarnings();

        SaveOneConnection();
        SaveOneConnection();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1), "the user is told");
            Assert.That(WeakEncryptionWarnings(), Is.EqualTo(1), "and told once");
        });
    }

    [Test]
    public void AnUpgradedDatabaseIsNotWarnedAbout()
    {
        SeedDatabaseAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion);

        SaveOneConnection();

        Assert.That(WeakEncryptionWarnings(), Is.Zero);
    }

    /// <summary>Puts the database at a version, through the same path the application uses.</summary>
    private void SeedDatabaseAt(Version version)
    {
        _storeKey = CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(version)
            ? MasterPassword
            : new RootNodeInfo(RootNodeType.Connection).DefaultPassword;

        _retriever.GetDatabaseMetaData(_connector);
        _retriever.WriteDatabaseMetaData(RootOnTheStoreKey(), _connector, null, version);
    }

    private void SaveOneConnection()
    {
        ConnectionTreeModel model = new();
        RootNodeInfo root = RootOnTheStoreKey();
        root.AddChild(new ConnectionInfo { Name = "server", Password = ConnectionPassword });
        model.AddRootNode(root);

        new SqlConnectionsSaver(
            new SaveFilter(),
            Substitute.For<ISerializer<IEnumerable<LocalConnectionPropertiesModel>, string>>(),
            Substitute.For<IDataProvider<string>>()).Save(model);
    }

    /// <summary>
    /// A root node on the key the seeded database uses. Setting <c>PasswordString</c> to the default
    /// leaves <c>Password</c> false, which is exactly the unprotected state a legacy store records —
    /// so this expresses both cases without a branch.
    /// </summary>
    private RootNodeInfo RootOnTheStoreKey() =>
        new(RootNodeType.Connection) { PasswordString = _storeKey };

    private SecureString MasterKey() => _storeKey.ConvertToSecureString();

    private string DecryptStoredPasswordWith(ICryptographyProvider provider) =>
        provider.Decrypt(StoredPasswordCiphertext(), MasterKey());

    private string StoredPasswordCiphertext()
    {
        using DbCommand command = _connector.DbCommand("SELECT TOP 1 Password FROM tblCons");
        return (string)command.ExecuteScalar()!;
    }

    private Version RecordedVersion() => _retriever.GetDatabaseMetaData(_connector)!.ConfVersion;

    private static int WeakEncryptionWarnings() =>
        Runtime.MessageCollector.Messages.Count(m =>
            m.Class == MessageClass.WarningMsg &&
            m.Text.Contains("weak encryption", StringComparison.Ordinal));
}
