using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml;
using mRemoteNG.App.Info;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// What happens to a store protected by its own key when the load fails and the backup set comes
/// into play.
/// </summary>
/// <remarks>
/// The distinction the whole section rests on: a key that cannot be unwrapped is not a corrupt file.
/// Every backup is a byte-identical copy carrying the same protectors, so a protector failure fails
/// identically for every one of them — and recovery ends by overwriting the live file, so reaching
/// that path for the wrong reason destroys the store it was trying to save.
/// </remarks>
[TestFixture]
public class PerFileKeyBackupRecoveryTests
{
    private const int FastIterations = 1000;
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-backup-" + TestContext.CurrentContext.Test.ID);
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
    public void APlainCopyOfAProtectedStoreStillOpens()
    {
        // Task 6.1. FileBackupCreator is a File.Copy and stays one — a backup carrying both
        // protectors is already restorable anywhere, and rewriting one per backup would put key
        // handling into the one mechanism whose whole value is that it cannot go wrong. Asserted
        // rather than assumed.
        WriteProtectedStore(machineProtector: true);
        string backup = Path.Combine(_directory, "confCons.xml.copy.backup");
        File.Copy(_storePath, backup);

        ConnectionTreeModel restored = new XmlConnectionsLoader(backup, new MessageCollector(), NeverAsked).Load();

        Assert.That(restored.RootNodes.OfType<RootNodeInfo>().First().Children.First().Password,
            Is.EqualTo("hunter2"));
    }

    [Test]
    public void ABackupRestoredWhereTheMachineProtectorCannotHelpOpensOnTheRecoveryPassword()
    {
        // Task 6.4. The property the second protector exists for: a copy that has left the machine
        // is still the user's to open.
        WriteProtectedStore(machineProtector: false);
        string backup = Path.Combine(_directory, "confCons.xml.copy.backup");
        File.Copy(_storePath, backup);

        int prompts = 0;
        ConnectionTreeModel restored = new XmlConnectionsLoader(backup, new MessageCollector(),
            _ => { prompts++; return "recovery".ConvertToSecureString(); }).Load();

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(restored.RootNodes.OfType<RootNodeInfo>().First().Children.First().Password,
                Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void ABackupRestoredOnTheMachineThatWroteItOpensWithNoPrompt()
    {
        WriteProtectedStore(machineProtector: true);
        string backup = Path.Combine(_directory, "confCons.xml.copy.backup");
        File.Copy(_storePath, backup);

        ConnectionTreeModel restored = new XmlConnectionsLoader(backup, new MessageCollector(), NeverAsked).Load();

        Assert.That(restored.RootNodes.OfType<RootNodeInfo>().First().Children, Is.Not.Empty);
    }

    [Test]
    public void AProtectorFailureIsReportedOnceRatherThanOncePerBackup()
    {
        // Task 6.4, and the defect this section exists to prevent. Ten rolling backups all fail for
        // the same reason, so walking them produces ten warnings implying the whole set is corrupt
        // when every file is intact — and up to three password prompts each.
        WriteProtectedStore(machineProtector: false);
        for (int i = 0; i < 5; i++)
            File.Copy(_storePath, Path.Combine(_directory, $"confCons.xml.2026080{i}.backup"));

        MessageCollector collector = new();
        int prompts = 0;

        Assert.Throws<KeyProtectionException>(() => new XmlConnectionsLoader(_storePath, collector,
            _ => { prompts++; return "wrong".ConvertToSecureString(); }).Load());

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(3),
                "three attempts on the live file, and none at all on the five backups");
            Assert.That(collector.Messages.Count(m => m.Class == MessageClass.WarningMsg
                                                      && m.Text.Contains("backup", StringComparison.OrdinalIgnoreCase)),
                Is.Zero, "and no backup was reported as unreadable");
        });
    }

    [Test]
    public void AProtectorFailureLeavesTheLiveFileExactlyWhereItWas()
    {
        // Task 6.3. Recovery ends with File.Copy(backup, liveFile, overwrite: true). Reaching it for
        // the wrong reason overwrites the store the user was trying to open — and the backup that
        // replaced it would be just as unopenable.
        WriteProtectedStore(machineProtector: false);
        byte[] before = File.ReadAllBytes(_storePath);
        File.Copy(_storePath, Path.Combine(_directory, "confCons.xml.20260801.backup"));

        Assert.Throws<KeyProtectionException>(() => new XmlConnectionsLoader(_storePath, new MessageCollector(),
            _ => "wrong".ConvertToSecureString()).Load());

        Assert.That(File.ReadAllBytes(_storePath), Is.EqualTo(before));
    }

    [Test]
    public void AnUnreadableStoreStillFallsBackToItsBackups()
    {
        // The counterweight. Refusing to search on a protector failure must not turn into refusing to
        // search at all — recovery from a genuinely corrupt file is what the mechanism is for.
        WriteProtectedStore(machineProtector: true);
        File.Copy(_storePath, Path.Combine(_directory, "confCons.xml.20260801.backup"));

        // Corrupt, but still carrying the declaration — a truncated write rather than a replaced
        // file. Damage without the declaration takes a different path entirely, which
        // ACorruptFileWithNoXmlDeclarationStillPromptsForAPassword pins.
        File.WriteAllText(_storePath, "<?xml version=\"1.0\" encoding=\"utf-8\"?><mrng:Connections");

        ConnectionTreeModel recovered = new XmlConnectionsLoader(_storePath, new MessageCollector(), NeverAsked).Load();

        Assert.Multiple(() =>
        {
            Assert.That(recovered.RootNodes.OfType<RootNodeInfo>().First().Children, Is.Not.Empty);
            Assert.That(File.ReadAllText(_storePath), Does.Not.Contain("<not-valid-xml"),
                "and the recovered backup replaced the damaged file, which is what recovery is");
        });
    }

    [Test]
    public void AProtectedBackupDoesNotOverwriteTheLiveFileWhenItCannotBeUnwrapped()
    {
        // The two halves together: an empty live file sends the loader to the backup set, and the
        // first backup that cannot be unwrapped stops it. Nothing is copied over the live file.
        WriteProtectedStore(machineProtector: false);
        for (int i = 0; i < 4; i++)
            File.Copy(_storePath, Path.Combine(_directory, $"confCons.xml.2026080{i}.backup"));
        File.WriteAllText(_storePath, "");

        MessageCollector collector = new();
        int prompts = 0;

        Assert.Throws<XmlException>(() => new XmlConnectionsLoader(_storePath, collector,
            _ => { prompts++; return "wrong".ConvertToSecureString(); }).Load());

        Assert.Multiple(() =>
        {
            // Three attempts against the first backup, then the walk stops. Without that, four
            // backups cost twelve prompts and four warnings about files that are perfectly intact.
            Assert.That(prompts, Is.EqualTo(3), "the remaining three backups were not attempted");
            Assert.That(File.ReadAllText(_storePath), Is.Empty, "the live file was not overwritten");
            Assert.That(collector.Messages.Any(m => m.Class == MessageClass.ErrorMsg
                                                    && m.Text.Contains("recovery password", StringComparison.OrdinalIgnoreCase)),
                "and the message names the password rather than blaming the backup");
        });
    }

    [Test]
    public void ACorruptFileWithNoXmlDeclarationStillPromptsForAPassword()
    {
        // **This pins a limitation, not a desired behaviour.** LegacyFullFileDecrypt treats anything
        // without the exact `<?xml version="1.0" encoding="utf-8"?>` declaration as encrypted, so
        // damage that removes the declaration is met with a decrypt attempt and then a password
        // prompt — before the store's protectors have been looked at, and before the parse failure
        // that would have sent it to the backup set.
        //
        // Pre-existing and not introduced by the per-file key; it is why RefuseUnrecognisedStorageFormat
        // reads the raw text ahead of that call. Recorded here because it was raised in review as
        // theoretical and turned out to be reachable — the first draft of
        // AnUnreadableStoreStillFallsBackToItsBackups hit it. A fix means restructuring the load
        // order every existing format depends on, so this test exists to make that change visible
        // when someone makes it.
        //
        // The cost is narrower than it first looks, which is the other half of why it is recorded
        // rather than fixed here: the spurious prompt can be dismissed, and the load then fails to
        // parse and recovers from the backup set exactly as it should. A confusing question, not a
        // lost file.
        WriteProtectedStore(machineProtector: true);
        File.Copy(_storePath, Path.Combine(_directory, "confCons.xml.20260801.backup"));
        File.WriteAllText(_storePath, "<mrng:Connections truncated");

        int prompts = 0;
        ConnectionTreeModel recovered = new XmlConnectionsLoader(_storePath, new MessageCollector(),
            _ => { prompts++; return Optional<SecureString>.Empty; }).Load();

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.GreaterThan(0),
                "if this stops prompting, the load order was fixed — delete this test and say so");
            Assert.That(recovered.RootNodes.OfType<RootNodeInfo>().First().Children, Is.Not.Empty,
                "and the dismissed prompt does not stop the backup recovery that follows");
        });
    }

    private void WriteProtectedStore(bool machineProtector)
    {
        // A store with no machine protector is one `MachineProtectorPolicy` declined, and since 7.5
        // the saver adopts one when the policy would allow it. The test temp directory lives under
        // the user profile, so without saying which edition this is, the protector these tests need
        // to fail would quietly succeed.
        if (!machineProtector)
            PortableEdition.OverrideForTests(true);

        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, "recovery".ConvertToSecureString(), machineProtector, FastIterations)
        };
        root.AddChild(new ConnectionInfo { Name = "server", Password = "hunter2" });
        model.AddRootNode(root);

        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);
    }

    private static Optional<SecureString> NeverAsked(string _)
    {
        Assert.Fail("no password should have been requested");
        return Optional<SecureString>.Empty;
    }
}
