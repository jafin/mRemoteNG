using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using mRemoteNG.App.Info;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// When a connection file's secrets are decrypted. Characterisation, not aspiration.
/// </summary>
/// <remarks>
/// <para>
/// <b>This fixture records behaviour that is not what the change proposing it assumed.</b> The
/// proposal states that decryption is deferred per field rather than run eagerly over the whole
/// file, and cites a line that no longer holds that code. What the deserializer actually does is
/// collect every encrypted attribute while it walks the XML and then decrypt <i>all of them in one
/// batch</i> before the load returns — <c>ProcessPendingDecrypts</c>. The deferral is real, but it
/// is a batching of the key derivation for speed, not a narrowing of how long secrets are in memory.
/// </para>
/// <para>
/// So opening a connection file puts every password it holds into the process, and the batch step
/// materialises them as a <c>string[]</c> — immutable, unzeroable, alive until collected — before
/// each one is copied into its record's <see cref="SecureString"/>. That is a wider exposure than
/// the property-getter copies the change was written to narrow, and it is worth having written down
/// rather than assumed away.
/// </para>
/// <para>
/// These tests pass against today's code and are meant to fail the day it changes, in either
/// direction: if lazy decryption is ever implemented, the assertion that reports eager decryption is
/// where the news arrives.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionSecretDecryptionTimingTests
{
    private const int FastIterations = 1000;
    private static readonly string[] EverySecretInTheStore = ["first-secret", "second-secret", "third-secret"];
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-secret-timing-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "confCons.xml");
        PortableEdition.OverrideForTests(false);
    }

    [TearDown]
    public void Teardown()
    {
        RecoveryPasswordSession.Clear();
        MachineSlotSession.Forget();
        PortableEdition.OverrideForTests(null);
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void LoadingAFileDecryptsEverySecretInIt()
    {
        // Read the SecureString field rather than the property: the getter is exactly where a lazy
        // implementation would do its decrypting, so asking it says nothing about when the work
        // happened. Nothing touches a password between the load and this read, so the only thing
        // that can have filled those fields is the load itself. If this ever fails, decryption
        // became lazy and the capability's second requirement became true — update the spec rather
        // than this test.
        SaveStoreWithPasswords();

        ConnectionTreeModel opened = Reopen();

        string[] stored = [.. Connections(opened).Select(StoredSecret)];

        Assert.That(stored, Is.EqualTo(EverySecretInTheStore),
            "every password was in memory before anything asked for one");
    }

    [Test]
    public void ASecretIsDecryptedEvenForAConnectionNeverOpened()
    {
        // The consequence, stated as a user would meet it: a file of two hundred connections puts
        // two hundred passwords in the process to serve the two the user actually connects to.
        SaveStoreWithPasswords();

        ConnectionTreeModel opened = Reopen();
        ConnectionInfo neverUsed = Connections(opened).Last();
        string storedBeforeAnyoneAsked = StoredSecret(neverUsed);

        Assert.Multiple(() =>
        {
            Assert.That(storedBeforeAnyoneAsked, Is.EqualTo("third-secret"),
                "the last connection's secret was decrypted by the load, not by the read below");
            Assert.That(neverUsed.Password, Is.EqualTo("third-secret"));
        });
    }

    [Test]
    public void ADecryptedSecretIsHeldAsASecureStringOnTheRecord()
    {
        // What the load does get right, and the half the audit's scan cannot see: the plain text it
        // produces is copied into a SecureString and the record never holds a string field.
        SaveStoreWithPasswords();

        ConnectionInfo connection = Connections(Reopen()).First();
        SecureString? stored = StoredSecureString(connection);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Length, Is.EqualTo("first-secret".Length));
        });
    }

    private void SaveStoreWithPasswords()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, "recovery".ConvertToSecureString(), includeMachineProtector: true, FastIterations)
        };

        root.AddChild(new ConnectionInfo { Name = "one", Password = "first-secret" });
        root.AddChild(new ConnectionInfo { Name = "two", Password = "second-secret" });
        root.AddChild(new ConnectionInfo { Name = "three", Password = "third-secret" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);
    }

    private ConnectionTreeModel Reopen() =>
        new XmlConnectionsDeserializer(_storePath, NeverAsked)
            .Deserialize(File.ReadAllText(_storePath));

    /// <summary>
    /// The record's own <see cref="SecureString"/>, read without going through the property that
    /// would decrypt it if decryption were lazy.
    /// </summary>
    private static SecureString? StoredSecureString(ConnectionInfo connection) =>
        (SecureString?)typeof(AbstractConnectionRecord)
            .GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection);

    private static string StoredSecret(ConnectionInfo connection) =>
        StoredSecureString(connection)?.ConvertToUnsecureString() ?? "";

    private static ConnectionInfo[] Connections(ConnectionTreeModel model) =>
        [.. model.RootNodes.OfType<RootNodeInfo>().First().Children];

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the machine protector should have opened the store");
        return Optional<SecureString>.Empty;
    }
}
