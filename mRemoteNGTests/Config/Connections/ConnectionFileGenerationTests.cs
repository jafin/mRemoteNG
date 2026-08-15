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
/// A save from a session that has not seen a rekey, and the ordinary saves that must not be caught
/// by the same net.
/// </summary>
/// <remarks>
/// <para>
/// The rekey is the operation that removes a member's access, and it is undone completely by one
/// ordinary save from a colleague who had the file open before it — their session still holds the old
/// key and the old protectors, and saving writes the whole store back under them. Nothing is visible
/// afterwards: the file is well-formed, opens, and opens on the password the rekey retired.
/// </para>
/// <para>
/// Two sessions cannot be run in one process, so the second session is played by a second
/// <see cref="ConnectionTreeModel"/> loaded from the same file before the rekey — which is exactly
/// what a second session is, minus the second machine.
/// </para>
/// </remarks>
[TestFixture]
public class ConnectionFileGenerationTests
{
    private const int FastIterations = 1000;
    private string _directory = "";
    private string _storePath = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-generation-" + TestContext.CurrentContext.Test.ID);
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
    public void AHardenedStoreRecordsWhichKeyItIsOn()
    {
        SaveNewStore();

        Assert.That(XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.GenerationAttributeName)?.Value,
            Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ARekeyChangesItAndNothingElseDoes()
    {
        SaveNewStore();
        string atFirst = Generation();

        // An ordinary save, and a save that earns a slot: neither is a new key, so neither may look
        // like one. If they did, every other member's next save would be refused for a rekey that
        // never happened.
        ConnectionTreeModel opened = Reopen(NeverAsked);
        Save(opened);
        Assert.That(Generation(), Is.EqualTo(atFirst), "an ordinary save is not a new key");

        Rekey(opened, "second password");
        Save(opened);

        Assert.That(Generation(), Is.Not.EqualTo(atFirst));
    }

    [Test]
    public void ASaveFromBeforeARekeyIsRefused()
    {
        // The whole reason the generation exists. Without it this save succeeds silently and the
        // rekey is undone — the old recovery password opens the file again, and the member it was
        // meant to remove is back, with nobody aware of it.
        SaveNewStore();
        ConnectionTreeModel theirSession = Reopen(NeverAsked);

        ConnectionTreeModel mySession = Reopen(NeverAsked);
        Rekey(mySession, "second password");
        Save(mySession);

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => Save(theirSession))!;

        Assert.Multiple(() =>
        {
            Assert.That(refused.Message, Does.Contain("rekeyed"),
                "and says why, because the alternative reads as a bug in saving");
            Assert.That(refused.Message, Does.Contain("still here"),
                "and says the unsaved work is not lost, which is the user's first question");
        });
    }

    [Test]
    public void ARefusedSaveLeavesTheRekeyedFileExactlyAsItWas()
    {
        // Refusing is worth nothing if the write has already happened. The point of the check is what
        // is *not* on disk afterwards: the old protectors, wrapping the old key.
        SaveNewStore();
        ConnectionTreeModel theirSession = Reopen(NeverAsked);

        ConnectionTreeModel mySession = Reopen(NeverAsked);
        Rekey(mySession, "second password");
        Save(mySession);
        string afterRekey = File.ReadAllText(_storePath);

        Assert.Throws<InvalidOperationException>(() => Save(theirSession));

        Assert.That(File.ReadAllText(_storePath), Is.EqualTo(afterRekey), "not one byte");

        // And the retired password is still retired.
        RecoveryPasswordSession.Clear();
        MachineSlotSession.Forget();
        Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(
                ProtectionOnDisk().RecoveryProtector, Password("first password")));
    }

    [Test]
    public void TheSessionThatRekeyedCanSaveTwice()
    {
        // The generation on disk is now ahead of the one this session read, and it is this session
        // that put it there. Comparing against what it currently holds instead of what it read would
        // let a rekey be saved exactly once and then lock its own author out of their file.
        SaveNewStore();
        ConnectionTreeModel mine = Reopen(NeverAsked);

        Rekey(mine, "second password");
        Save(mine);

        Assert.DoesNotThrow(() => Save(mine));
    }

    [Test]
    public void AnOrdinarySaveFromAnotherSessionIsNotRefused()
    {
        // Task 4.2. Two people editing one file is not a security boundary and never has been: the
        // later save wins, as it did before any of this existed. Only a rekey refuses anything.
        SaveNewStore();
        ConnectionTreeModel theirSession = Reopen(NeverAsked);
        ConnectionTreeModel mySession = Reopen(NeverAsked);

        Save(mySession);

        Assert.DoesNotThrow(() => Save(theirSession));
    }

    [Test]
    public void TwoSessionsSavingInTurnLeaveAFileBothCanStillOpen()
    {
        // Task 4.5's first case, and the cost of having no locking: the later save carries only the
        // slots its own session knew about, so a slot can be lost. What must survive is the file
        // itself — it opens, on the key it always had, and the lost slot costs one prompt.
        SaveNewStore();
        ConnectionTreeModel theirSession = Reopen(NeverAsked);
        ConnectionTreeModel mySession = Reopen(NeverAsked);

        Save(mySession);
        Save(theirSession);

        RecoveryPasswordSession.Clear();
        MachineSlotSession.Forget();
        Assert.That(Connection(Reopen(NeverAsked)).Password, Is.EqualTo("hunter2"),
            "the machine protector still opens it");

        RecoveryPasswordSession.Clear();
        MachineSlotSession.Forget();
        Assert.DoesNotThrow(() => RecoveryPasswordKeyProtector
            .Unwrap(ProtectionOnDisk().RecoveryProtector, Password("first password")).Dispose(),
            "and so does the recovery password");
    }

    [Test]
    public void AFileWrittenBeforeGenerationsExistedIsSavedNormally()
    {
        // Every store hardened by the preceding change. It carries no generation, so there is nothing
        // to compare and nothing to refuse — and it must not acquire one on an ordinary save, which
        // would hand every other session holding it a token they had never seen.
        SaveNewStore();
        StripGeneration();

        ConnectionTreeModel opened = Reopen(NeverAsked);

        Assert.DoesNotThrow(() => Save(opened));
        Assert.That(XElement.Load(_storePath)
            .Attribute(ConnectionFileKeyProtection.GenerationAttributeName), Is.Null,
            "and it stays as it was until something actually changes the key");
    }

    [Test]
    public void RekeyingAFileWrittenBeforeGenerationsExistedStillRefusesTheStaleSave()
    {
        // The upgrade path, and the case that would be easy to get wrong: nothing on either side to
        // compare at first, so the protection has to come from the rekey giving the file its first
        // generation rather than from the stale session having read one.
        SaveNewStore();
        StripGeneration();

        ConnectionTreeModel theirSession = Reopen(NeverAsked);
        ConnectionTreeModel mySession = Reopen(NeverAsked);
        Rekey(mySession, "second password");
        Save(mySession);

        Assert.Throws<InvalidOperationException>(() => Save(theirSession));
    }

    private void SaveNewStore()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, Password("first password"), includeMachineProtector: true, FastIterations)
        };
        root.AddChild(new ConnectionInfo { Name = "server", Password = "hunter2" });
        model.AddRootNode(root);

        Save(model);
    }

    private static void Rekey(ConnectionTreeModel model, string newPassword) =>
        ConnectionFileRekey.Apply(Root(model), Password(newPassword),
                                  includeMachineProtector: true, FastIterations);

    private void Save(ConnectionTreeModel model) =>
        new XmlConnectionsSaver(_storePath, new SaveFilter()).Save(model);

    private ConnectionTreeModel Reopen(Func<Optional<SecureString>> requestor) =>
        new XmlConnectionsDeserializer(_storePath, requestor)
            .Deserialize(File.ReadAllText(_storePath));

    private string Generation() => XElement.Load(_storePath)
        .Attribute(ConnectionFileKeyProtection.GenerationAttributeName)!.Value;

    /// <summary>Makes the file look as the preceding change wrote it: protected, and with no generation.</summary>
    private void StripGeneration()
    {
        XElement root = XElement.Load(_storePath);
        root.Attribute(ConnectionFileKeyProtection.GenerationAttributeName)!.Remove();
        root.Save(_storePath);
    }

    private ConnectionFileKeyProtection ProtectionOnDisk()
    {
        XElement root = XElement.Load(_storePath);
        return ConnectionFileKeyProtection.Read(
            root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)?.Value,
            root.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName)?.Value,
            root.Attribute(ConnectionFileKeyProtection.GenerationAttributeName)?.Value)!;
    }

    private static RootNodeInfo Root(ConnectionTreeModel model) =>
        model.RootNodes.OfType<RootNodeInfo>().First();

    private static ConnectionInfo Connection(ConnectionTreeModel model) => Root(model).Children.First();

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("this account's machine protector should have opened the store");
        return Optional<SecureString>.Empty;
    }
}
