using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NSubstitute;
using NUnit.Framework;
using ConnectionInfoAlias = mRemoteNG.Connection.ConnectionInfo;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// That a database using authenticated encryption is opened with a master password, and never with
/// the key built into mRemoteNG.
/// </summary>
/// <remarks>
/// <para>
/// The default key is four characters published in this application's source. A shared database
/// keyed with it is readable by everyone holding SELECT on the connections table, which on a team
/// database is routinely more people than are trusted with the credentials it stores — and nothing
/// in the interface said so, because a store encrypted under a published constant looks exactly
/// like a store that is encrypted.
/// </para>
/// <para>
/// Written against the loader's own control flow with substitutes, because that is what decides
/// this: which key is tried, in what order, and whether the user is asked at all. How it behaves
/// against a real database belongs to the SQL integration group.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlMasterPasswordRequirementTests
{
    private const string Master = "the database master password";
    private const string ConnectionPassword = "hunter2";

    private static readonly Version LegacyVersion = new(3, 5);

    private static readonly Version AuthenticatedVersion =
        CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion;

    private IDeserializer<string, IEnumerable<LocalConnectionPropertiesModel>> _localProperties = null!;
    private IDataProvider<string> _localPropertiesProvider = null!;
    private IDatabaseConnector _connector = null!;
    private IDataProvider<DataTable> _dataProvider = null!;
    private ISqlDatabaseMetaDataRetriever _metaDataRetriever = null!;
    private ISqlDatabaseVersionVerifier _versionVerifier = null!;

    private int _prompts;

    [SetUp]
    public void Setup()
    {
        _localProperties = Substitute.For<IDeserializer<string, IEnumerable<LocalConnectionPropertiesModel>>>();
        _localPropertiesProvider = Substitute.For<IDataProvider<string>>();
        _connector = Substitute.For<IDatabaseConnector>();
        _dataProvider = Substitute.For<IDataProvider<DataTable>>();
        _metaDataRetriever = Substitute.For<ISqlDatabaseMetaDataRetriever>();
        _versionVerifier = Substitute.For<ISqlDatabaseVersionVerifier>();

        _localProperties.Deserialize(Arg.Any<string>()).Returns(new List<LocalConnectionPropertiesModel>());
        _versionVerifier.VerifyDatabaseVersion(Arg.Any<Version>()).Returns(true);
        _prompts = 0;
    }

    [Test]
    public void AnUpgradedDatabaseOpensWithItsMasterPassword()
    {
        GivenDatabase(AuthenticatedVersion, Master);

        ConnectionTreeModel loaded = Loader(Answering(Master)).Load();

        Assert.Multiple(() =>
        {
            Assert.That(Passwords(loaded), Does.Contain(ConnectionPassword));
            Assert.That(_prompts, Is.EqualTo(1), "asked once, and only once");
        });
    }

    [Test]
    public void AnUpgradedDatabaseLoadsNothingWhenThePasswordIsNotSupplied()
    {
        // Declining the prompt has to end the load. Falling through to the built-in key would be
        // the same defect reached by another route, and it would present as success.
        GivenDatabase(AuthenticatedVersion, Master);

        Assert.Throws<InvalidOperationException>(() => Loader(Declining()).Load());
    }

    [Test]
    public void AnUpgradedDatabaseLoadsNothingWhenThePasswordIsWrong()
    {
        GivenDatabase(AuthenticatedVersion, Master);

        Assert.Throws<InvalidOperationException>(() => Loader(Answering("not the master password")).Load());
    }

    [Test]
    public void TheDefaultKeyIsNotTriedAtTheAuthenticatedVersion()
    {
        // **The defect, stated as the loader sees it.** This database's sentinel is written under
        // the published default key, so the old code opened it without asking anyone anything. What
        // is asserted is that the user is asked even though a key that works is sitting in the
        // source: the version decides, not what happens to decrypt.
        GivenDatabase(AuthenticatedVersion, ConnectionFileDefaults.LegacyEncryptionKey);

        Assert.Throws<InvalidOperationException>(() => Loader(Declining()).Load());
        Assert.That(_prompts, Is.EqualTo(1), "the user was asked rather than the default key tried");
    }

    [Test]
    public void AnUpgradedDatabaseWithNoSentinelIsRefusedRatherThanOpened()
    {
        // An empty sentinel below the new version means "this database has no master password". At
        // the new version it cannot mean that, because there is no unprotected state to record — it
        // means the metadata row was lost. Returning the default key here would open a database
        // that is meant to require one.
        GivenDatabase(AuthenticatedVersion, masterPassword: null);

        Assert.Throws<InvalidOperationException>(() => Loader(Answering(Master)).Load());
        Assert.That(_prompts, Is.Zero,
            "and nothing is asked for, because there is nothing left to check an answer against");
    }

    [Test]
    public void ALegacyDatabaseWithNoMasterPasswordStillOpensWithoutBeingAsked()
    {
        // The other half, and a far worse defect than the one being fixed if it broke: refusing
        // these would destroy a team's access to their connections in order to change how those
        // connections are stored.
        GivenDatabase(LegacyVersion, masterPassword: null);

        ConnectionTreeModel loaded = Loader(Declining()).Load();

        Assert.Multiple(() =>
        {
            Assert.That(Passwords(loaded), Does.Contain(ConnectionPassword));
            Assert.That(_prompts, Is.Zero);
        });
    }

    [Test]
    public void ALegacyDatabaseWithAMasterPasswordStillAsksForIt()
    {
        GivenDatabase(LegacyVersion, Master);

        ConnectionTreeModel loaded = Loader(Answering(Master)).Load();

        Assert.That(Passwords(loaded), Does.Contain(ConnectionPassword));
    }

    [Test]
    public void AnUnprotectedStoreIsNotRecordedAtTheAuthenticatedVersion()
    {
        // The write side of the same rule. The "unprotected" sentinel is encrypted under the root
        // node's own password, which for a tree with none is the built-in default — so recording it
        // at this version produces a database that claims modern encryption while being keyed on a
        // published constant, and an interface that reports it as protected.
        //
        // Refused rather than quietly written at the older version: silently downgrading the format
        // to accommodate a missing password is how a store ends up weaker than its version marker
        // claims. Below this version the same call still writes, which every test in the SQL
        // integration group depends on to seed a legacy database.
        Assert.Throws<InvalidOperationException>(() => new SqlDatabaseMetaDataRetriever()
            .WriteDatabaseMetaData(new RootNodeInfo(RootNodeType.Connection), _connector, null,
                                   AuthenticatedVersion));
    }

    /// <summary>
    /// A database at the given version, keyed on the given master password — or holding no sentinel
    /// at all and keyed on the built-in default, when that is null.
    /// </summary>
    private void GivenDatabase(Version version, string? masterPassword)
    {
        ICryptographyProvider provider = CryptoProviderFactoryFromSqlVersion.ProviderFor(version);
        SecureString key = (masterPassword ?? ConnectionFileDefaults.LegacyEncryptionKey).ConvertToSecureString();

        _metaDataRetriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>()).Returns(new SqlConnectionListMetaData
        {
            Name = "Connections",
            ConfVersion = version,
            Export = false,
            Protected = masterPassword is null
                ? ""
                : provider.Encrypt(ConnectionFileDefaults.ProtectedSentinel, key)
        });

        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfoAlias
        {
            Name = "webserver",
            Hostname = "web01",
            Password = ConnectionPassword
        });

        ConnectionTreeModel model = new();
        model.AddRootNode(root);

        _dataProvider.Load().Returns(new DataTableSerializer(new SaveFilter(), provider, key).Serialize(model));
    }

    private SqlConnectionsLoader Loader(Func<string, Optional<SecureString>> authenticationRequestor) =>
        new(_localProperties, _localPropertiesProvider, _connector, _dataProvider, _metaDataRetriever,
            _versionVerifier, CryptoProviderFactoryFromSqlVersion.ProviderFor, authenticationRequestor);

    private Func<string, Optional<SecureString>> Answering(string password) =>
        _ =>
        {
            _prompts++;
            return new Optional<SecureString>(password.ConvertToSecureString());
        };

    private Func<string, Optional<SecureString>> Declining() =>
        _ =>
        {
            _prompts++;
            return Optional<SecureString>.Empty;
        };

    private static IEnumerable<string> Passwords(ConnectionTreeModel model) =>
        model.RootNodes.SelectMany(root => root.GetRecursiveChildList()).Select(c => c.Password);
}
