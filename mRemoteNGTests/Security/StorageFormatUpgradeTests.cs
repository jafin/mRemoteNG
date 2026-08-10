using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.KeyDerivation;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

/// <summary>
/// What the upgrade confirmation says, and what it does with the answer. Both are tested away from
/// the dialog, which is the only way a test can assert the wording of a security warning at all.
/// </summary>
[TestFixture]
public class StorageFormatUpgradeTests
{
    [Test]
    public void TheWarningNamesTheApplicationsThatStopWorking()
    {
        // The sentence a user can actually decide on. Naming the key derivation function instead
        // would be true and useless.
        StorageFormatUpgradeMessage message =
            StorageFormatUpgrade.BuildMessage(StorageFormatStoreKind.ConnectionFile);

        Assert.Multiple(() =>
        {
            Assert.That(message.Content, Does.Contain("mRemoteNG"));
            Assert.That(message.Content, Does.Contain("earlier builds"));
        });
    }

    [Test]
    public void TheWarningSaysWhatHappensToBackups()
    {
        // The upstream-readability of a whole backup set changes at the moment of upgrade. A user
        // who believes their backups are a way out needs telling that only the old ones are.
        StorageFormatUpgradeMessage message =
            StorageFormatUpgrade.BuildMessage(StorageFormatStoreKind.ConnectionFile);

        Assert.That(message.Content, Does.Contain("Backups"));
    }

    [Test]
    public void TheCryptographyIsBelowThePartThatMatters()
    {
        // Present for those who want it, not competing with the sentence about applications.
        StorageFormatUpgradeMessage message =
            StorageFormatUpgrade.BuildMessage(StorageFormatStoreKind.ConnectionFile);

        Assert.Multiple(() =>
        {
            Assert.That(message.ExpandedInfo, Does.Contain("PBKDF2"));
            Assert.That(message.Content, Does.Not.Contain("PBKDF2"));
        });
    }

    [Test]
    public void ASqlStoreAlsoWarnsAboutEveryOtherClient()
    {
        // The person confirming is not the only person affected, and cannot undo it for the others.
        StorageFormatUpgradeMessage sql = StorageFormatUpgrade.BuildMessage(StorageFormatStoreKind.SqlDatabase);
        StorageFormatUpgradeMessage file = StorageFormatUpgrade.BuildMessage(StorageFormatStoreKind.ConnectionFile);

        Assert.Multiple(() =>
        {
            Assert.That(sql.Content, Does.Contain("client"));
            Assert.That(file.Content, Does.Not.Contain("client"));
        });
    }

    [Test]
    public void TheButtonsAreThreeAndNoLabelSplitsItself()
    {
        // The task dialog splits its button list on the pipe, so a pipe inside a label silently
        // becomes an extra button and shifts every choice after it by one — on this dialog, a click
        // meant for the export would harden the store. Caught in the real dialog, not by a test,
        // which is why there is now a test.
        string[] buttons = StorageFormatUpgrade.CommandButtons().Split('|');

        Assert.That(buttons, Has.Length.EqualTo(3));
        Assert.That(buttons, Has.All.Not.Empty);
    }

    [Test]
    public void ConfirmingRaisesTheLevel()
    {
        RootNodeInfo root = new(RootNodeType.Connection);

        bool raised = StorageFormatUpgrade.Apply(root, StorageFormatUpgradeChoice.Harden);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.True);
            Assert.That(root.StorageFormat, Is.EqualTo(StorageFormatLevel.Hardened));
        });
    }

    [TestCase(StorageFormatUpgradeChoice.Decline)]
    [TestCase(StorageFormatUpgradeChoice.ExportClassicCopy)]
    public void AnythingElseLeavesTheStoreClassic(StorageFormatUpgradeChoice choice)
    {
        // Taking the classic copy is not consent to harden. A user who asked for the way back before
        // deciding has not decided, and hardening on that click would be the silent upgrade the
        // whole change exists to prevent.
        RootNodeInfo root = new(RootNodeType.Connection);

        bool raised = StorageFormatUpgrade.Apply(root, choice);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.False);
            Assert.That(root.StorageFormat, Is.EqualTo(StorageFormatLevel.Classic));
        });
    }

    [Test]
    public void DecliningLeavesTheStoreByteCompatibleWithUpstream()
    {
        // The claim is about the file, not about a field: after declining, what gets written must
        // carry no attribute upstream mRemoteNG has never seen.
        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfo { Name = "server" });
        ConnectionTreeModel model = new();
        model.AddRootNode(root);

        StorageFormatUpgrade.Apply(root, StorageFormatUpgradeChoice.Decline);

        var cryptoProvider = new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();
        XmlConnectionNodeSerializer28 nodeSerializer = new(
            cryptoProvider, root.PasswordString.ConvertToSecureString(), new SaveFilter());
        string serialized = new XmlConnectionsSerializer(cryptoProvider, nodeSerializer).Serialize(model);

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Not.Contain(StorageFormat.AttributeName));
            Assert.That(serialized, Does.Not.Contain(KeyDerivationPrf.AttributeName));
        });
    }
}
