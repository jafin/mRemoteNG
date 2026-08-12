using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml.Linq;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// A store protected by its own random key, written and read back through the real save and load
/// paths.
/// </summary>
/// <remarks>
/// The unit tests around the protectors prove the key survives being wrapped. These prove the file
/// is actually keyed on it: that the legacy default key no longer opens anything, that the sentinel
/// is what an unwrapped key is checked against, and that an export drops the protectors rather than
/// carrying a machine-bound one into the escape route.
/// </remarks>
[TestFixture]
public class PerFileKeyStoreRoundTripTests
{
    private const int FastIterations = 1000;
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-perfilekey-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "confCons.xml");
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void AStoreKeyedOnItselfRoundTripsAndAsksForNothing()
    {
        // The daily path end to end. The machine protector opens it, so nothing is requested — which
        // is the property that decides whether this design is acceptable to live with.
        (ConnectionTreeModel model, _) = ProtectedStore("server", "hunter2");
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

        ConnectionTreeModel reopened = Reopen(NeverAsked);
        ConnectionInfo connection = reopened.RootNodes.OfType<RootNodeInfo>().First().Children.First();

        Assert.Multiple(() =>
        {
            Assert.That(connection.Name, Is.EqualTo("server"));
            Assert.That(connection.Password, Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void TheFileDeclaresThePerFileKeyAndCarriesBothProtectors()
    {
        (ConnectionTreeModel model, _) = ProtectedStore("server", "hunter2");
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

        XElement root = XElement.Load(_storePath);

        Assert.Multiple(() =>
        {
            Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Not.Null);
            Assert.That(root.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName), Is.Not.Null);
            Assert.That(root.Attribute(StorageFormat.AttributeName)?.Value, Is.EqualTo("Hardened"));
        });
    }

    [Test]
    public void TheLegacyKeyOpensNothingInAStoreThatHasItsOwnKey()
    {
        // Task 4.3. The point of the whole change: mR3m is published in this application's source, so
        // a file it still opens is a file anyone who copied it can read.
        (ConnectionTreeModel model, _) = ProtectedStore("server", "hunter2");
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

        XElement root = XElement.Load(_storePath);
        string protectedAttribute = root.Attribute("Protected")!.Value;
        string passwordAttribute = root.Descendants().First().Attribute("Password")!.Value;

        using SecureString legacyKey = ConnectionFileDefaults.LegacyEncryptionKey.ConvertToSecureString();
        AeadCryptographyProvider legacy = new() { KeyDerivationIterations = 1000 };

        Assert.Multiple(() =>
        {
            Assert.Throws<EncryptionException>(() => legacy.Decrypt(protectedAttribute, legacyKey),
                "the protection declaration is not readable under the legacy key");
            Assert.Throws<EncryptionException>(() => legacy.Decrypt(passwordAttribute, legacyKey),
                "and neither is a stored password");
        });
    }

    [Test]
    public void AClassicStoreIsStillWrittenUnderTheLegacyKey()
    {
        // The counterweight, and the reason the legacy key stays reachable. A classic store has to
        // keep opening in upstream mRemoteNG, which reads this same file from this same path.
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfo { Name = "server", Password = "hunter2" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

        XElement saved = XElement.Load(_storePath);
        using SecureString legacyKey = ConnectionFileDefaults.LegacyEncryptionKey.ConvertToSecureString();
        AeadCryptographyProvider legacy = new()
        {
            KeyDerivationIterations = mRemoteNG.Properties.OptionsSecurityPage.Default.EncryptionKeyDerivationIterations
        };

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Decrypt(saved.Attribute("Protected")!.Value, legacyKey),
                Is.EqualTo(ConnectionFileDefaults.NotProtectedSentinel));
            Assert.That(saved.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null);
        });
    }

    [Test]
    public void AnExportOfAProtectedStoreCarriesNoProtectors()
    {
        // Task 1.2. An export is the way back, so it states the classic level rather than inheriting
        // the store's — and that has to take the protectors off with it. A copy carrying a
        // machine-bound protector is a copy that opens on one machine, which is the opposite of what
        // an escape route is for.
        (ConnectionTreeModel model, _) = ProtectedStore("server", "hunter2");
        RootNodeInfo root = model.RootNodes.OfType<RootNodeInfo>().First();

        ISerializer<ConnectionInfo, string> exporter = XmlConnectionSerializerFactory.Build(
            new AeadCryptographyProvider { KeyDerivationIterations = 1000 }, model);
        ((XmlConnectionsSerializer)exporter).StorageFormatOverride = StorageFormatLevel.Classic;

        XElement exported = XElement.Parse(exporter.Serialize(root));

        Assert.Multiple(() =>
        {
            Assert.That(exported.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null);
            Assert.That(exported.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName), Is.Null);
            Assert.That(exported.Attribute(StorageFormat.AttributeName), Is.Null,
                "and it states the classic level, which is what took the protectors off");
        });
    }

    [Test]
    public void AProtectorHoldingAnotherFilesKeyIsRejectedRatherThanUsed()
    {
        // Task 5.7. A protector unwrapping proves this account may use it, not that it belongs to
        // this file. Without the sentinel check the store opens onto contents that cannot be
        // decrypted, with no prompt and nothing reported.
        (ConnectionTreeModel model, _) = ProtectedStore("server", "hunter2");
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

        using ConnectionFileKey strangerKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection stranger = ConnectionFileKeyProtection.Create(
            strangerKey, "stranger".ConvertToSecureString(), iterations: FastIterations);

        XElement root = XElement.Load(_storePath);
        stranger.WriteTo(root);
        root.Save(_storePath);

        // Both protectors now hold a key this file was not written with, and the recovery password
        // for them is known — so the failure cannot be blamed on the password being wrong.
        Assert.Throws<KeyProtectionException>(() => Reopen(() => "stranger".ConvertToSecureString()));
    }

    [Test]
    public void AStoreWhoseKeyIsNoLongerAvailableRefusesToSave()
    {
        // Falling back to the settings provider here would encrypt the contents under the master
        // password while the root still declared the per-file sentinel and carried protectors
        // wrapping a key nothing was encrypted with — a file that passes every structural check and
        // decrypts to nothing.
        (ConnectionTreeModel model, ConnectionFileKey key) = ProtectedStore("server", "hunter2");
        model.RootNodes.OfType<RootNodeInfo>().First().FileKey = null;
        key.Dispose();

        Assert.Throws<InvalidOperationException>(
            () => new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model));
    }

    private static (ConnectionTreeModel Model, ConnectionFileKey Key) ProtectedStore(string name, string password)
    {
        ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = fileKey,
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, "recovery".ConvertToSecureString(), iterations: FastIterations)
        };
        root.AddChild(new ConnectionInfo { Name = name, Password = password });
        model.AddRootNode(root);
        return (model, fileKey);
    }

    private ConnectionTreeModel Reopen(Func<Optional<SecureString>> requestor) =>
        new XmlConnectionsDeserializer(_storePath, requestor)
            .Deserialize(File.ReadAllText(_storePath));

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the machine protector should have opened this store without a prompt");
        return Optional<SecureString>.Empty;
    }
}
