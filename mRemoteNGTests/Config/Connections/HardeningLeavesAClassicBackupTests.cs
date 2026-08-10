using System.IO;
using System.Linq;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.KeyDerivation;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections;

/// <summary>
/// The confirmation tells the user that backups taken before hardening stay readable by upstream
/// mRemoteNG. That is a promise about the save which does the hardening, and it is only true if the
/// backup is taken from the file as it was, before the hardened content replaces it.
/// </summary>
[TestFixture]
public class HardeningLeavesAClassicBackupTests
{
    private string _directory;
    private string _storePath;

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-harden-backup-" + TestContext.CurrentContext.Test.ID);
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
    public void TheSaveThatHardensAStoreBacksUpTheClassicFileFirst()
    {
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfo { Name = "server" });
        model.AddRootNode(root);

        XmlConnectionsSaver saver = new(_storePath, new SaveFilter());

        // A classic store, saved once. This is the file the user has before they are asked.
        saver.Save(model);
        Assert.That(File.ReadAllText(_storePath), Does.Not.Contain(StorageFormat.AttributeName),
            "the store should be classic before hardening");

        // Confirming raises the level; the store is rewritten by the next save.
        StorageFormatUpgrade.Apply(root, StorageFormatUpgradeChoice.Harden);
        saver.Save(model);

        Assert.That(File.ReadAllText(_storePath), Does.Contain(StorageFormat.AttributeName),
            "the store should be hardened after");

        string[] backups = Directory.GetFiles(_directory, "*.backup");

        Assert.That(backups, Is.Not.Empty,
            "hardening must leave a backup, or the confirmation's promise about existing backups is empty");

        // The newest backup is the copy taken by the hardening save. It has to be the classic file
        // the user had a moment earlier — a hardened backup would be no way back at all.
        string newest = backups.OrderByDescending(File.GetLastWriteTimeUtc).First();
        string contents = File.ReadAllText(newest);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Not.Contain(StorageFormat.AttributeName));
            Assert.That(contents, Does.Not.Contain(KeyDerivationPrf.AttributeName));
        });
    }
}
