using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using mRemoteNG.App.Info;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// What a save writes for a secret nothing ever decrypted.
/// </summary>
/// <remarks>
/// <para>
/// Two answers, and choosing between them wrongly in one direction is the only way this change can
/// destroy anything. Under the key the bytes were read with, writing them back untouched is correct
/// and free. Under a <i>different</i> key it produces a file whose contents are encrypted under two
/// keys, and the half under the old key can never be decrypted again.
/// </para>
/// <para>
/// So the ordinary-save tests here are about cost, and the rekey and hardening tests are about the
/// file surviving at all.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionSecretPassThroughTests
{
    private const int FastIterations = 1000;
    private static readonly string[] EverySecretInTheStore = ["first-secret", "second-secret", "third-secret"];
    private static readonly string[] EverySecretInTheClassicStore = ["first-secret", "second-secret"];
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-secret-passthrough-" + TestContext.CurrentContext.Test.ID);
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
    public void AnOrdinarySaveOfAnUntouchedStoreLeavesEverySecretByteIdentical()
    {
        SaveHardenedStore();
        string[] before = StoredPasswords();

        ConnectionTreeModel reopened = Reopen();
        Save(reopened);

        Assert.Multiple(() =>
        {
            Assert.That(StoredPasswords(), Is.EqualTo(before),
                "nothing read them, so there was nothing to re-encrypt");
            Assert.That(Connections(reopened).Select(StoredSecureString), Is.All.Null,
                "and the save did not decrypt them in order to write them");
        });
    }

    [Test]
    public void EditingOneConnectionRewritesThatOneAndPassesTheRestThrough()
    {
        SaveHardenedStore();
        string[] before = StoredPasswords();

        ConnectionTreeModel reopened = Reopen();
        Connections(reopened)[1].Password = "replaced";
        Save(reopened);

        string[] after = StoredPasswords();

        Assert.Multiple(() =>
        {
            Assert.That(after[0], Is.EqualTo(before[0]));
            Assert.That(after[2], Is.EqualTo(before[2]));
            Assert.That(after[1], Is.Not.EqualTo(before[1]));
            Assert.That(Connections(Reopen())[1].Password, Is.EqualTo("replaced"));
        });
    }

    [Test]
    public void ARekeyDecryptsEverySecretBeforeTheOldKeyIsThrownAway()
    {
        // The key the ciphertext was read under is disposed by the rekey. Anything still holding
        // ciphertext at that moment is unrecoverable, so the decryption has to happen first - not at
        // the save that follows, by which time there is nothing left to decrypt it with.
        SaveHardenedStore();
        ConnectionTreeModel reopened = Reopen();

        ConnectionFileRekey.Apply(Root(reopened), "second-recovery".ConvertToSecureString(),
                                  includeMachineProtector: true, FastIterations);

        Assert.That(Connections(reopened).Select(StoredSecureString), Is.All.Not.Null);
    }

    [Test]
    public void AfterARekeyTheWholeFileDecryptsUnderTheNewKey()
    {
        SaveHardenedStore();
        ConnectionTreeModel reopened = Reopen();

        ConnectionFileRekey.Apply(Root(reopened), "second-recovery".ConvertToSecureString(),
                                  includeMachineProtector: true, FastIterations);
        Save(reopened);

        Assert.That(Connections(Reopen()).Select(c => c.Password),
                    Is.EqualTo(EverySecretInTheStore));
    }

    [Test]
    public void ARekeyIsRefusedWhenASecretCannotBeDecryptedAndLeavesTheStoreAsItWas()
    {
        SaveHardenedStore();
        CorruptTheStoredPasswordOf("two");
        ConnectionTreeModel reopened = Reopen();
        RootNodeInfo root = Root(reopened);

        ConnectionFileKey? keyBefore = root.FileKey;
        ConnectionFileKeyProtection? protectionBefore = root.KeyProtection;
        string fileBefore = File.ReadAllText(_storePath);

        Assert.Throws<ConnectionSecretDecryptionException>(() =>
            ConnectionFileRekey.Apply(root, "second-recovery".ConvertToSecureString(),
                                      includeMachineProtector: true, FastIterations));

        Assert.Multiple(() =>
        {
            Assert.That(root.FileKey, Is.SameAs(keyBefore), "the store still holds the key it opened with");
            Assert.That(root.KeyProtection, Is.SameAs(protectionBefore));
            Assert.That(File.ReadAllText(_storePath), Is.EqualTo(fileBefore));
        });
    }

    [Test]
    public void HardeningAClassicStoreReEncryptsSecretsNothingEverRead()
    {
        // Hardening replaces a key derived from a password with a random key of the store's own, so
        // every secret in the file has to be re-encrypted - including the ones this session never
        // had a reason to decrypt.
        ConnectionTreeModel model = new();
        RootNodeInfo classic = new(RootNodeType.Connection);
        classic.AddChild(new ConnectionInfo { Name = "one", Password = "first-secret" });
        classic.AddChild(new ConnectionInfo { Name = "two", Password = "second-secret" });
        model.AddRootNode(classic);
        Save(model);

        ConnectionTreeModel reopened = Reopen();
        RootNodeInfo root = Root(reopened);

        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        root.StorageFormat = StorageFormatLevel.Hardened;
        root.FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes);
        root.KeyProtection = ConnectionFileKeyProtection.Create(
            fileKey, "recovery".ConvertToSecureString(), includeMachineProtector: true, FastIterations);

        Save(reopened);

        Assert.That(Connections(Reopen()).Select(c => c.Password),
                    Is.EqualTo(EverySecretInTheClassicStore));
    }

    [Test]
    public void ASaveThatCannotDecryptASecretItMustReEncryptIsRefused()
    {
        // The other half of the rekey rule: where a key change reaches the serializer rather than
        // the rekey, a secret that will not decrypt has to stop the write rather than be skipped.
        SaveHardenedStore();
        CorruptTheStoredPasswordOf("two");

        ConnectionTreeModel reopened = Reopen();
        RootNodeInfo root = Root(reopened);

        // A different key of the same kind: the identity no longer matches, so every secret must be
        // decrypted and encrypted afresh.
        using ConnectionFileKey replacement = ConnectionFileKey.Generate();
        root.FileKey = ConnectionFileKey.FromBytes(replacement.Bytes);

        Assert.Throws<ConnectionSecretDecryptionException>(() => Save(reopened));
    }

    [Test]
    public void ASerializerGivenNoKeyIdentityNeverWritesStoredCipherTextBack()
    {
        // Every caller that builds this serializer by hand - the export path is the one that matters
        // - gets the safe answer without having to know the rule exists.
        ConnectionInfo connection = new() { Name = "exported" };
        connection.SetPendingSecret(nameof(ConnectionInfo.Password),
            new PendingConnectionSecret("stored-ciphertext", AnyKey(), _ => "plain"));

        Assert.That(connection.TryGetStoredCipherText(nameof(ConnectionInfo.Password), null, out _), Is.False);
    }

    private static ConnectionSecretKeyIdentity AnyKey() =>
        ConnectionSecretKeyIdentity.For(new mRemoteNG.Security.SymmetricEncryption.LegacyRijndaelCryptographyProvider(),
                                        fileKey: null, password: "any");

    private void SaveHardenedStore()
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

        Save(model);
    }

    private void Save(ConnectionTreeModel model) =>
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

    private ConnectionTreeModel Reopen() =>
        new XmlConnectionsDeserializer(_storePath, NeverAsked)
            .Deserialize(File.ReadAllText(_storePath));

    /// <summary>
    /// The stored <c>Password</c> attribute of every connection, in document order.
    /// </summary>
    private string[] StoredPasswords() =>
        [.. Regex.Matches(File.ReadAllText(_storePath), "<Node[^>]*?\\sPassword=\"(?<value>[^\"]*)\"",
                          RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5))
                 .Select(m => m.Groups["value"].Value)];

    private void CorruptTheStoredPasswordOf(string connectionName)
    {
        string xml = File.ReadAllText(_storePath);
        Regex attribute = new($"(?<head>Name=\"{connectionName}\"[^>]*?Password=\")[^\"]*(?<tail>\")",
                              RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));

        Assert.That(attribute.IsMatch(xml), Is.True,
            "the store was expected to hold this password as a readable attribute");

        File.WriteAllText(_storePath, attribute.Replace(xml, "${head}bm90LWEtY2lwaGVydGV4dA==${tail}", 1));
    }

    private static RootNodeInfo Root(ConnectionTreeModel model) =>
        model.RootNodes.OfType<RootNodeInfo>().First();

    private static ConnectionInfo[] Connections(ConnectionTreeModel model) => [.. Root(model).Children];

    private static SecureString? StoredSecureString(ConnectionInfo connection) =>
        (SecureString?)typeof(AbstractConnectionRecord)
            .GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(connection);

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the store should have opened without a prompt");
        return Optional<SecureString>.Empty;
    }
}
