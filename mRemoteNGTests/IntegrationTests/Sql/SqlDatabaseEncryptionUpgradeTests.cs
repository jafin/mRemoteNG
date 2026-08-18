using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sql;

/// <summary>
/// Upgrading a database's encryption, against a real one.
/// </summary>
/// <remarks>
/// <para>
/// This is the operation that cannot be half-done. It reads every secret with the legacy provider,
/// writes it back authenticated, and raises the version marker — and if any part of that lands
/// without the rest, the marker claims one format while the contents are a mixture, with nothing to
/// say which rows are which. A test against a substitute cannot show that: the transaction is the
/// mechanism, so the database has to be real.
/// </para>
/// <para>
/// The wrong-password case is the other one worth having. The legacy provider is AES-CBC with no
/// authentication tag, so it does not refuse a wrong key — it returns rubbish. Without the sentinel
/// check first, an upgrade with the wrong password would decrypt every secret to rubbish and write
/// that rubbish back under the new format, destroying the database with no error at any point.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlDatabaseEncryptionUpgradeTests
{
    private const string FirstPassword = "hunter2";
    private const string SecondPassword = "correct horse battery staple";

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

        _retriever.GetDatabaseMetaData(_connector);
        _retriever.WriteDatabaseMetaData(new RootNodeInfo(RootNodeType.Connection), _connector, null,
                                         SqlDatabaseVersionVerifier.SchemaVersion);
    }

    [TearDown]
    public void Teardown() => _connector?.Dispose();

    [Test]
    public void EverySecretIsReEncryptedAndTheVersionIsRaised()
    {
        InsertLegacyConnection("one", FirstPassword);
        InsertLegacyConnection("two", SecondPassword);

        int rewritten = SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Is.EqualTo(2));
            Assert.That(Version(), Is.EqualTo(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));
            Assert.That(Aead().Decrypt(StoredPassword("one"), DefaultKey()), Is.EqualTo(FirstPassword));
            Assert.That(Aead().Decrypt(StoredPassword("two"), DefaultKey()), Is.EqualTo(SecondPassword));
        });
    }

    [Test]
    public void TheSentinelMovesWithTheRowsSoTheDatabaseStillOpens()
    {
        // The sentinel is read with the same provider as the rows, so leaving it behind would make
        // the upgraded database refuse the password it was upgraded with. It is also the field most
        // worth moving: a fixed, published plaintext under an unsalted MD5 key is an ideal offline
        // cracking oracle for whoever can read the table.
        InsertLegacyConnection("one", FirstPassword);

        SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever);

        SqlConnectionListMetaData metaData = _retriever.GetDatabaseMetaData(_connector)!;

        Assert.That(Aead().Decrypt(metaData.Protected, DefaultKey()),
            Is.EqualTo(ConnectionFileDefaults.NotProtectedSentinel));
    }

    [Test]
    public void AnEmptyColumnStaysEmptyRatherThanBecomingAnEncryptedNothing()
    {
        // "This connection has no gateway password" and "this connection's gateway password is the
        // empty string" mean the same thing to this application and different things to anything
        // comparing the columns — a diff, a backup, a report.
        InsertLegacyConnection("one", FirstPassword);

        SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever);

        Assert.That(StoredColumn("one", "RDGatewayPassword"), Is.Empty);
    }

    [Test]
    public void AWrongPasswordChangesNothing()
    {
        // Needs a database that actually has a master password. An earlier version of this test
        // seeded an unprotected database and expected the wrong password to be rejected — it is not,
        // and should not be: with no master password the store opens on the built-in default key and
        // whatever the caller passed is irrelevant. The test was wrong, not the code.
        SeedMasterPassword("the real master password");
        InsertLegacyConnection("one", FirstPassword);
        string before = StoredPassword("one");

        Assert.Throws<EncryptionException>(() =>
            SqlDatabaseEncryptionUpgrade.Apply(_connector, "not the master password".ConvertToSecureString(),
                                               _retriever));

        Assert.Multiple(() =>
        {
            Assert.That(StoredPassword("one"), Is.EqualTo(before), "not one byte");
            Assert.That(Version(), Is.EqualTo(SqlDatabaseVersionVerifier.SchemaVersion),
                "and the database is still legacy, so it still opens");
        });
    }

    [Test]
    public void TheRightPasswordUpgradesAProtectedDatabaseAndKeepsItProtected()
    {
        // The other half, and the one that would fail silently: if the rewritten sentinel said
        // "not protected", the next open would accept the built-in default key instead of asking
        // for the master password — the upgrade would have removed the master password.
        const string master = "the real master password";
        SeedMasterPassword(master);
        InsertLegacyConnection("one", FirstPassword, master);

        SqlDatabaseEncryptionUpgrade.Apply(_connector, master.ConvertToSecureString(), _retriever);

        SqlConnectionListMetaData metaData = _retriever.GetDatabaseMetaData(_connector)!;

        Assert.Multiple(() =>
        {
            Assert.That(Aead().Decrypt(metaData.Protected, master.ConvertToSecureString()),
                Is.EqualTo(ConnectionFileDefaults.ProtectedSentinel), "still protected");
            Assert.That(Aead().Decrypt(StoredPassword("one"), master.ConvertToSecureString()),
                Is.EqualTo(FirstPassword), "and the secret came across under the same password");
        });
    }

    [Test]
    public void AFailureMidWayRollsBackToAWhollyLegacyDatabase()
    {
        // The state this exists to prevent. One row is left holding something the legacy provider
        // cannot decrypt, so the upgrade fails partway — after earlier rows have already been
        // rewritten inside the transaction. What must survive is a database that is entirely legacy
        // and entirely readable, which is where it started.
        InsertLegacyConnection("one", FirstPassword);
        InsertRawPassword("two", "this is not base64 and never was");
        string firstBefore = StoredPassword("one");

        Assert.Throws<EncryptionException>(
            () => SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever));

        Assert.Multiple(() =>
        {
            Assert.That(StoredPassword("one"), Is.EqualTo(firstBefore),
                "the row that was already rewritten is back as it was");
            Assert.That(Legacy().Decrypt(StoredPassword("one"), DefaultKey()), Is.EqualTo(FirstPassword),
                "and still decrypts with the provider the database declares");
            Assert.That(Version(), Is.EqualTo(SqlDatabaseVersionVerifier.SchemaVersion),
                "and the marker did not move ahead of the contents");
        });
    }

    [Test]
    public void AnAlreadyUpgradedDatabaseIsRefusedRatherThanReEncrypted()
    {
        // Running it twice would decrypt AEAD ciphertext with the legacy provider. That does not
        // fail cleanly — AES-CBC has no tag — so it would write rubbish over every secret.
        InsertLegacyConnection("one", FirstPassword);
        SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever);

        Assert.Throws<ArgumentException>(
            () => SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever));
    }

    [Test]
    public void AnEmptyDatabaseUpgradesToo()
    {
        // Nothing to rewrite, but the marker still moves — otherwise the next save would write the
        // legacy format into a database somebody has just chosen to upgrade.
        int rewritten = SqlDatabaseEncryptionUpgrade.Apply(_connector, DefaultKey(), _retriever);

        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Is.Zero);
            Assert.That(Version(),
                Is.EqualTo(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));
        });
    }

    private static SecureString DefaultKey() =>
        new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();

    private static LegacyRijndaelCryptographyProvider Legacy() => new();

    private static AeadCryptographyProvider Aead() => new();

    private void InsertLegacyConnection(string id, string password, string? masterPassword = null) =>
        InsertRawPassword(id, Legacy().Encrypt(password,
            masterPassword is null ? DefaultKey() : masterPassword.ConvertToSecureString()));

    /// <summary>Gives the database a master password, by writing the sentinel that records one.</summary>
    private void SeedMasterPassword(string masterPassword)
    {
        RootNodeInfo root = new(RootNodeType.Connection) { Password = true, PasswordString = masterPassword };
        _retriever.WriteDatabaseMetaData(root, _connector, null, SqlDatabaseVersionVerifier.SchemaVersion);
    }

    /// <summary>
    /// Inserts a connection row carrying the given ciphertext, filling every other required column
    /// from the schema.
    /// </summary>
    /// <remarks>
    /// <b>Built from <c>INFORMATION_SCHEMA</c> rather than written out.</b> <c>tblCons</c> has
    /// dozens of NOT NULL columns and gains more as the schema version rises, so a hand-written
    /// INSERT is a list that goes stale — and it goes stale as a test failure about a column name,
    /// in a test about encryption. Asking the database what it requires means this keeps working
    /// when the schema moves.
    /// <para>
    /// The row is inserted directly rather than through the serializer on purpose: the upgrade has
    /// to work on rows already in the table, whatever put them there.
    /// </para>
    /// </remarks>
    private void InsertRawPassword(string id, string cipherText)
    {
        List<string> columns = ["ConstantID", "Name", "Password"];
        List<string> values = ["@id", "@id", "@pw"];

        foreach ((string column, string type) in RequiredColumns())
        {
            if (columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                continue;

            columns.Add(column);
            values.Add(type switch
            {
                "datetime" or "datetime2" or "date" => "GETDATE()",
                "int" or "bigint" or "smallint" or "tinyint" or "bit" => "0",
                _ => "''"
            });
        }

        using DbCommand command = _connector.DbCommand(
            $"INSERT INTO tblCons ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})");

        DbParameter idParameter = command.CreateParameter();
        idParameter.ParameterName = "@id";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);

        DbParameter passwordParameter = command.CreateParameter();
        passwordParameter.ParameterName = "@pw";
        passwordParameter.DbType = System.Data.DbType.String;
        passwordParameter.Size = -1;
        passwordParameter.Value = cipherText;
        command.Parameters.Add(passwordParameter);

        command.ExecuteNonQuery();
    }

    /// <summary>Columns the schema insists on and supplies no default for.</summary>
    private List<(string Column, string Type)> RequiredColumns()
    {
        List<(string, string)> required = [];

        using DbCommand command = _connector.DbCommand(
            "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'tblCons' AND IS_NULLABLE = 'NO' " +
            "AND COLUMN_DEFAULT IS NULL AND COLUMNPROPERTY(OBJECT_ID(TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 0 " +
            // rowversion/timestamp is maintained by the engine and refuses an explicit value, so it
            // is NOT NULL, has no default, and still must not appear in the column list.
            "AND DATA_TYPE <> 'timestamp'");

        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
            required.Add(((string)reader["COLUMN_NAME"], (string)reader["DATA_TYPE"]));

        return required;
    }

    private string StoredPassword(string id) => StoredColumn(id, "Password");

    private string StoredColumn(string id, string column)
    {
        using DbCommand command = _connector.DbCommand(
            $"SELECT {column} FROM tblCons WHERE ConstantID = @id");

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);

        // DBNull, not empty string: a nullable secret column that was never written holds NULL, and
        // the upgrade treats the two the same — there is nothing to re-encrypt either way.
        object? value = command.ExecuteScalar();
        return value is DBNull or null ? string.Empty : (string)value;
    }

    private Version Version() => _retriever.GetDatabaseMetaData(_connector)!.ConfVersion;
}
