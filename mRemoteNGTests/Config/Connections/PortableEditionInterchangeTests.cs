using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml.Linq;
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
        // Static, so leaving it set would decide the edition for every test that ran afterwards.
        PortableEdition.OverrideForTests(null);
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
        //
        // **This exercises the API directly, and that is now the narrower claim.** It calls
        // `WithMachineProtector` rather than going through the saver, so it says the capability is
        // correct and nothing about whether the application uses it. For a long time nothing did,
        // and this test reading as coverage of that scenario is what hid the gap until 8.4 was run
        // by hand. The application side is task 7.5, asserted through the saver by
        // `SavingInsideTheProfileGivesARecoveryOnlyStoreItsMachineProtector` and the three tests
        // beside it. Both are worth keeping: this one fails if the wrapping breaks, those fail if
        // nothing calls it.
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

    [Test]
    public void SavingInsideTheProfileGivesARecoveryOnlyStoreItsMachineProtector()
    {
        // Task 7.5, through the saver. Until this existed, `WithMachineProtector` had no caller in
        // the application at all: a store that arrived with only a recovery protector — moved in
        // from outside the profile, written by the portable edition, or migrated before a profile
        // rebuild — prompted on every open for ever, and nothing could change that.
        Assert.That(MachineProtectorPolicy.IsInsideUserProfile(_storePath),
            "the temp directory must be inside the user profile or this asserts nothing");

        PortableEdition.OverrideForTests(false);
        SaveRecoveryOnlyStore();

        XElement root = XElement.Load(_storePath);

        Assert.Multiple(() =>
        {
            Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Not.Null,
                "the store was entitled to a machine protector and had none");
            Assert.That(root.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName), Is.Not.Null,
                "and the recovery protector is untouched, so every existing copy still opens");
        });

        // The property that makes adopting safe rather than merely convenient.
        Assert.That(Connection(Reopen(NeverAsked)).Password, Is.EqualTo("hunter2"));
    }

    [Test]
    public void AdoptingKeepsTheStoresOwnKeyAndItsRecoveryPassword()
    {
        // Adopting wraps the same file key a second way; it does not rekey the store. The claim is
        // not that the ciphertext is unchanged — it always changes, because the per-file provider is
        // AES-GCM with a fresh nonce per encryption and reusing one would be far worse than a churned
        // file. What must be unchanged is the *key*, and the recovery protector is where that shows:
        // a new key would force it to be rewrapped.
        PortableEdition.OverrideForTests(true);
        SaveRecoveryOnlyStore();
        string recoveryBefore = XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName)!.Value;

        PortableEdition.OverrideForTests(false);
        ConnectionTreeModel opened = Reopen(() => Password("recovery"));
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(opened);

        XElement after = XElement.Load(_storePath);

        Assert.Multiple(() =>
        {
            Assert.That(after.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Not.Null,
                "the store was adopted");
            Assert.That(after.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName)!.Value,
                Is.EqualTo(recoveryBefore),
                "and the recovery protector was not rewrapped, so it still holds the same key");
        });

        // Which means every copy taken before the adoption still opens on the same password.
        RecoveryPasswordSession.Clear();
        PortableEdition.OverrideForTests(true);
        Assert.That(Connection(Reopen(() => Password("recovery"))).Password, Is.EqualTo("hunter2"));
    }

    [Test]
    public void ThePortableEditionAdoptsNothing()
    {
        // The whole point of the edition: there is no account worth binding to on a machine the
        // application was carried to.
        PortableEdition.OverrideForTests(true);
        SaveRecoveryOnlyStore();

        Assert.That(XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null);
    }

    [Test]
    public void AMemberWhoOpenedWithTheRecoveryPasswordEarnsASlotOnSave()
    {
        // The shared-file promise, end to end and as close as one process can get to two accounts: a
        // store carrying somebody else's machine protector, opened here with the recovery password.
        // Before key slots this member was prompted on every open for ever, because the file had a
        // machine protector and it was not theirs — which is why the preceding change wrote none at
        // all for a file outside the profile.
        PortableEdition.OverrideForTests(false);
        Save(machineProtector: true);
        ReplaceMachineProtectorWithOneThisAccountCannotUse();
        string theirSlot = XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value;

        RecoveryPasswordSession.Clear();
        PortableEdition.OverrideForTests(false);
        ConnectionTreeModel opened = Reopen(() => Password("recovery"));
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(opened);

        string[] slots = XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value
            .Split(ConnectionFileKeyProtection.SlotSeparator);

        Assert.Multiple(() =>
        {
            Assert.That(slots, Has.Length.EqualTo(2), "this account's slot is added, not substituted");
            Assert.That(slots[0], Is.EqualTo(theirSlot),
                "the other member's slot is untouched, so their silent open survives this save");
        });

        // And the prompt is gone for this account from here on.
        RecoveryPasswordSession.Clear();
        PortableEdition.OverrideForTests(false);
        Assert.That(Connection(Reopen(NeverAsked)).Password, Is.EqualTo("hunter2"));
    }

    [Test]
    public void AStoreThatAlreadyHasAMachineProtectorIsLeftAlone()
    {
        // Re-wrapping would be harmless but wasteful, and it would churn the file on every save —
        // a new DPAPI blob each time means every backup differs from the last for no reason.
        PortableEdition.OverrideForTests(false);
        Save(machineProtector: true);
        PortableEdition.OverrideForTests(false);
        string blobBefore = XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value;

        ConnectionTreeModel opened = Reopen(NeverAsked);
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(opened);

        Assert.That(XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value,
            Is.EqualTo(blobBefore));
    }

    /// <summary>
    /// Writes a store carrying only its recovery protector, whatever edition is in force — so the
    /// saver's decision is the thing under test rather than what the store was built with.
    /// </summary>
    private void SaveRecoveryOnlyStore()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, Password("recovery"), includeMachineProtector: false, FastIterations)
        };
        root.AddChild(new ConnectionInfo { Name = "server", Password = "hunter2" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);
    }

    private void Save(bool machineProtector)
    {
        // The portable edition is the reason these stores have no machine protector, so say so
        // rather than relying on the saver not adding one. Since 7.5 it would: the test temp
        // directory is under the user profile, where the installed edition is entitled to adopt.
        PortableEdition.OverrideForTests(!machineProtector);

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
