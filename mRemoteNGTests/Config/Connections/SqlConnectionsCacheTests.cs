using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Config.Connections;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// How the local copy of a SQL store is protected.
/// </summary>
/// <remarks>
/// <para>
/// The cache holds every password in the store and is written on every successful load. Before this,
/// it was written through the ordinary connection-file saver using the database's own key — so for a
/// database with no master password, which is the common case, it was encrypted under a constant
/// published in mRemoteNG's source. Upgrading the database to authenticated encryption did nothing
/// for the copy sitting in the settings folder.
/// </para>
/// <para>
/// These write to a temporary folder through the <c>Location</c> seam. Without it they would put a
/// real connection store, passwords and all, into the developer's own settings directory.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlConnectionsCacheTests
{
    private const string Password = "hunter2";

    private string _location = "";
    private string _originalLocation = "";

    [SetUp]
    public void Setup()
    {
        _originalLocation = SqlConnectionsCache.Location;
        _location = Path.Combine(Path.GetTempPath(), "mrng-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_location);
        SqlConnectionsCache.Location = _location;
    }

    [TearDown]
    public void Teardown()
    {
        SqlConnectionsCache.Location = _originalLocation;

        try
        {
            if (Directory.Exists(_location))
                Directory.Delete(_location, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Test]
    public void TheCopyComesBackWithItsPasswords()
    {
        SqlConnectionsCache.Write(StoreWithNoMasterPassword(), new SaveFilter());

        ConnectionTreeModel restored = SqlConnectionsCache.Read();

        ConnectionInfo connection = restored.RootNodes
            .SelectMany(root => root.GetRecursiveChildList())
            .Single(c => string.Equals(c.Name, "webserver", StringComparison.Ordinal));

        Assert.That(connection.Password, Is.EqualTo(Password));
    }

    [Test]
    public void TheCopyIsNotReadableWithThePublishedKey()
    {
        // **The defect.** A store with no master password used to cache under
        // ConnectionFileDefaults.LegacyEncryptionKey, which is printed in mRemoteNG's own source, so
        // anyone with read access to the settings folder had every password in the team's database.
        SqlConnectionsCache.Write(StoreWithNoMasterPassword(), new SaveFilter());

        // Asserted as the property that matters rather than as "it throws". The loader refuses a
        // wrong password by declining to produce the store, not by raising — so a test written
        // around the exception would be testing the shape of the refusal instead of the secrecy of
        // the password, and would start passing for the wrong reason if that shape ever changed.
        string[] recovered = OpenWith(ConnectionFileDefaults.LegacyEncryptionKey);

        Assert.That(recovered, Does.Not.Contain(Password),
            "the published key must not yield the stored password");
    }

    [Test]
    public void TheCopyIsReadableWithItsOwnKey()
    {
        // The other half: the test above would also pass against a cache that was simply corrupt.
        SqlConnectionsCache.Write(StoreWithNoMasterPassword(), new SaveFilter());

        ConnectionTreeModel restored = SqlConnectionsCache.Read();

        Assert.That(restored.RootNodes.SelectMany(root => root.GetRecursiveChildList())
                .Select(connection => connection.Password),
            Does.Contain(Password));
    }

    [Test]
    public void WritingTheCopyLeavesTheStoresOwnKeyAlone()
    {
        // The cache is keyed by swapping a key onto the root node for the duration of the write,
        // because the saver takes the key from the root node it is given. If that swap ever failed
        // to unwind, the *database's* root would be left carrying the cache's key and the next save
        // would write the connection file under it.
        ConnectionTreeModel store = StoreWithNoMasterPassword();
        RootNodeInfo root = store.RootNodes.OfType<RootNodeInfo>().Single();

        bool passwordBefore = root.Password;
        string keyBefore = root.PasswordString;

        SqlConnectionsCache.Write(store, new SaveFilter());

        Assert.Multiple(() =>
        {
            Assert.That(root.PasswordString, Is.EqualTo(keyBefore));
            Assert.That(root.Password, Is.EqualTo(passwordBefore));
        });
    }

    [Test]
    public void ACopyWrittenBeforeThisVersionIsDeleted()
    {
        // Leaving it behind fixes nothing for the people who already have one — which is everybody
        // who has used the SQL backend.
        File.WriteAllText(SqlConnectionsCache.FilePath, "<Connections/>");

        SqlConnectionsCache.DiscardIfUnprotected();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(SqlConnectionsCache.FilePath), Is.False);
            Assert.That(SqlConnectionsCache.IsUsable, Is.False);
        });
    }

    [Test]
    public void ACopyWithItsKeyIsKept()
    {
        SqlConnectionsCache.Write(StoreWithNoMasterPassword(), new SaveFilter());

        SqlConnectionsCache.DiscardIfUnprotected();

        Assert.That(SqlConnectionsCache.IsUsable, "a protected copy is not what that clean-up is for");
    }

    [Test]
    public void ACopyWithoutItsKeyIsNotUsable()
    {
        SqlConnectionsCache.Write(StoreWithNoMasterPassword(), new SaveFilter());
        File.Delete(SqlConnectionsCache.FilePath + ".key");

        Assert.That(SqlConnectionsCache.IsUsable, Is.False,
            "unreadable is not the same as absent, and the fallback must not find out halfway through");
    }

    [Test]
    public void NoCopyAtAllIsNotUsable()
    {
        Assert.That(SqlConnectionsCache.IsUsable, Is.False);
    }

    /// <summary>
    /// Every password the cache gives up to the given key. Empty when it refuses altogether, which
    /// counts as giving up nothing.
    /// </summary>
    private static string[] OpenWith(string key)
    {
        try
        {
            return new XmlConnectionsLoader(SqlConnectionsCache.FilePath, null,
                    _ => new Optional<SecureString>(key.ConvertToSecureString()))
                .Load()
                .RootNodes.SelectMany(root => root.GetRecursiveChildList())
                .Select(connection => connection.Password)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>The case that was exposed: a database nobody set a master password on.</summary>
    private static ConnectionTreeModel StoreWithNoMasterPassword()
    {
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfo { Name = "webserver", Hostname = "web01", Password = Password });
        model.AddRootNode(root);
        return model;
    }
}
