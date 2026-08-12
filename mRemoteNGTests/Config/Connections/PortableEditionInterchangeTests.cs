using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml.Linq;
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

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// Files written by the portable edition and by the installed edition, each opened by the other.
/// </summary>
/// <remarks>
/// <para>
/// The old draft of this design had the two editions producing files neither could read. That is the
/// failure these tests exist to prevent, and it is not one a unit test on a protector would catch —
/// it lives in what the file says about itself. So everything here goes through the real save and
/// load paths and asserts against the file on disk.
/// </para>
/// <para>
/// <b>Where the proxy is.</b> "Opens on a second machine" cannot be run on one machine, and a test
/// that claimed to would be lying. What is asserted instead is the property that makes it true: the
/// file carries no machine-bound protector at all, and it opens with the recovery password alone. A
/// file with nothing bound to this machine in it has nothing that could fail on another. The
/// remaining step — carrying a real stick to a real second machine — is task 8.7, and stays manual
/// for exactly this reason.
/// </para>
/// </remarks>
[TestFixture]
public class PortableEditionInterchangeTests
{
    private const int FastIterations = 1000;
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-portable-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "confCons.xml");
    }

    [TearDown]
    public void Teardown()
    {
        RecoveryPasswordSession.Clear();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void APortableStoreWritesNoMachineProtectorIntoTheFile()
    {
        // Task 7.3. MachineProtectorPolicy already refuses the protector for the portable edition;
        // this is the other end of that decision, on the bytes. An empty attribute would be worse
        // than none — it reads back as a protector that fails, turning every portable open into a
        // fallback preceded by a message about a different Windows account.
        Save(machineProtector: false);

        XElement root = XElement.Load(_storePath);

        Assert.Multiple(() =>
        {
            Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null);
            Assert.That(root.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName), Is.Not.Null);
            Assert.That(File.ReadAllText(_storePath),
                Does.Not.Contain(ConnectionFileKeyProtection.MachineProtectorAttributeName),
                "not under any spelling — an empty attribute is a protector that always fails");
        });
    }

    [Test]
    public void APortableStoreOpensOnItsRecoveryPasswordAndNothingElse()
    {
        // Task 7.3, as close to a second machine as one machine gets: nothing in this file is bound
        // to this account, so the recovery password is the only thing that opened it.
        Save(machineProtector: false);

        int prompts = 0;
        ConnectionTreeModel opened = Reopen(() => { prompts++; return Password("recovery"); });

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(Connection(opened).Password, Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void AnInstalledStoreOpensInThePortableEditionWithItsRecoveryPassword()
    {
        // Task 7.4, first half. The portable edition has no usable machine protector wherever it is
        // run, which is indistinguishable from the installed file having been carried to another
        // machine — so the machine blob is replaced with one this account cannot unwrap. The recovery
        // protector is untouched, and it is what has to carry the open.
        Save(machineProtector: true);
        ReplaceMachineProtectorWithOneThisAccountCannotUse();

        int prompts = 0;
        ConnectionTreeModel opened = Reopen(() => { prompts++; return Password("recovery"); });

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1), "asked once, for the recovery password");
            Assert.That(Connection(opened).Password, Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void APortableStoreOpensInTheInstalledEditionAndCanBeGivenAMachineProtector()
    {
        // Task 7.4, second half. Opening is the easy part — the interesting one is that adopting the
        // file costs nothing: the installed edition wraps the same file key a second way, so the
        // contents are not re-encrypted and a backup taken before this still opens.
        Save(machineProtector: false);
        string encryptedPasswordBefore = StoredPasswordCiphertext();

        ConnectionTreeModel opened = Reopen(() => Password("recovery"));
        RootNodeInfo root = opened.RootNodes.OfType<RootNodeInfo>().First();

        XElement onDisk = XElement.Load(_storePath);
        root.KeyProtection!.WithMachineProtector(root.FileKey!).WriteTo(onDisk);
        onDisk.Save(_storePath);

        // Without this the session still holds the password from the open above, and a machine
        // protector that silently failed would be covered for by the cache — the test would pass on
        // the fallback it is meant to prove is no longer needed.
        RecoveryPasswordSession.Clear();

        ConnectionTreeModel afterwards = Reopen(NeverAsked);

        Assert.Multiple(() =>
        {
            Assert.That(Connection(afterwards).Password, Is.EqualTo("hunter2"));
            Assert.That(StoredPasswordCiphertext(), Is.EqualTo(encryptedPasswordBefore),
                "the same key, wrapped a second way — nothing the file holds was rewritten");
        });
    }

    [Test]
    public void TheTwoEditionsDoNotProduceAFormatSplit()
    {
        // Task 7.4's real subject. A build that could not read the other edition's file would be
        // discovered by users rather than by us, so the two files are compared directly: same
        // declared level, same sentinel, and exactly one attribute of difference between them.
        Save(machineProtector: true);
        XElement installed = XElement.Load(_storePath);

        File.Delete(_storePath);
        Save(machineProtector: false);
        XElement portable = XElement.Load(_storePath);

        string[] installedNames = [.. installed.Attributes().Select(a => a.Name.LocalName).Order(StringComparer.Ordinal)];
        string[] portableNames = [.. portable.Attributes().Select(a => a.Name.LocalName).Order(StringComparer.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(portable.Attribute(StorageFormat.AttributeName)?.Value,
                Is.EqualTo(installed.Attribute(StorageFormat.AttributeName)?.Value));
            Assert.That(installedNames.Except(portableNames, StringComparer.Ordinal),
                Is.EqualTo(new[] { ConnectionFileKeyProtection.MachineProtectorAttributeName }),
                "one attribute, and it is the optional one");
            Assert.That(portableNames.Except(installedNames, StringComparer.Ordinal), Is.Empty,
                "and the portable edition invents nothing of its own");
        });
    }

    private void Save(bool machineProtector)
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, Password("recovery"), machineProtector, FastIterations)
        };
        root.AddChild(new ConnectionInfo { Name = "server", Password = "hunter2" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);
    }

    /// <summary>
    /// Leaves a machine protector in place that is structurally sound and unusable — which is what
    /// this account sees of a blob written by another account, or by another machine entirely.
    /// </summary>
    private void ReplaceMachineProtectorWithOneThisAccountCannotUse()
    {
        XElement root = XElement.Load(_storePath);
        root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value =
            Convert.ToBase64String(new byte[256]);
        root.Save(_storePath);
    }

    private string StoredPasswordCiphertext() =>
        XElement.Load(_storePath).Descendants().First().Attribute("Password")!.Value;

    private ConnectionTreeModel Reopen(Func<Optional<SecureString>> requestor) =>
        new XmlConnectionsDeserializer(_storePath, requestor)
            .Deserialize(File.ReadAllText(_storePath));

    private static ConnectionInfo Connection(ConnectionTreeModel model) =>
        model.RootNodes.OfType<RootNodeInfo>().First().Children.First();

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the machine protector this edition just added should have opened the store");
        return Optional<SecureString>.Empty;
    }
}
