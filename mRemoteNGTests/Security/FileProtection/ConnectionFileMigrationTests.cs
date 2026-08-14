using System;
using System.IO;
using System.Security;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// The decisions the migration makes: whether the store gets a machine-bound protector at all, and
/// what the user is told before they are asked to choose a recovery password.
/// </summary>
/// <remarks>
/// Kept away from anything that shows a dialog, so the decisions can be asserted rather than
/// demonstrated — the same split <c>StorageFormatUpgrade</c> already keeps from its prompt.
/// </remarks>
[TestFixture]
public class ConnectionFileMigrationTests
{
    private const int FastIterations = 1000;

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static string InsideProfile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "AppData", "Roaming", "mRemoteNG", "confCons.xml");

    [Test]
    public void AStoreInTheUsersOwnProfileGetsBothProtectors()
    {
        Assert.That(MachineProtectorPolicy.ShouldWriteMachineProtector(InsideProfile, isPortableEdition: false));
    }

    [TestCase(@"\\fileserver\team\confCons.xml")]
    [TestCase(@"D:\Shared\confCons.xml")]
    [TestCase(@"C:\ProgramData\mRemoteNG\confCons.xml")]
    public void AStoreOutsideTheProfileNowGetsAMachineProtectorToo(string storePath)
    {
        // Reversed by key slots, and it is the point of them. This used to be false: the file could
        // carry one machine protector, so on a shared file it served exactly one person and cost
        // everyone else a prompt they had no way to remove, which made writing none the only
        // defensible answer. Each member now earns their own slot, so the location suppresses
        // nothing about what is written.
        Assert.That(MachineProtectorPolicy.ShouldWriteMachineProtector(storePath, isPortableEdition: false));
    }

    [Test]
    public void ThePortableEditionNeverGetsAMachineProtectorEvenInsideTheProfile()
    {
        // A build whose purpose is to run on whatever machine it is carried to has no account worth
        // binding to, wherever its file happens to sit at the moment.
        Assert.That(MachineProtectorPolicy.ShouldWriteMachineProtector(InsideProfile, isPortableEdition: true),
            Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a path with \0 an invalid character")]
    public void AStoreLocationThisCannotResolveIsTreatedAsOutside(string? storePath)
    {
        // Still "outside", and it still settles something — no longer what is written, but whether
        // the user is told that everyone sharing this file types the recovery password once each.
        // Warning someone about sharing a file that turns out not to be shared is the cheaper way to
        // be wrong.
        Assert.That(MachineProtectorPolicy.IsInsideUserProfile(storePath), Is.False);
    }

    [Test]
    public void AProfileOnAShareStillCountsAsInside()
    {
        // A roaming profile is not the case this is looking for. DPAPI at CurrentUser scope follows
        // the user's master key, which roams with the profile; what matters is a store the profile
        // does not travel with.
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(MachineProtectorPolicy.IsInsideUserProfile(Path.Combine(profile, "confCons.xml")));
    }

    [Test]
    public void MigratingGivesTheStoreANewKeyUnderBothProtectors()
    {
        RootNodeInfo root = new(RootNodeType.Connection);

        ConnectionFileMigration.Establish(root, Password("recovery"), includeMachineProtector: true, FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(root.FileKey, Is.Not.Null);
            Assert.That(root.KeyProtection, Is.Not.Null);
            Assert.That(root.KeyProtection!.HasMachineProtector);
            Assert.That(ConnectionFileMigration.IsAlreadyProtected(root));
        });
    }

    [Test]
    public void MigratingAlwaysGeneratesAFreshKey()
    {
        // Migration is the point at which the store stops being readable with the key published in
        // this application's source. Carrying anything forward from that state would defeat it.
        RootNodeInfo first = new(RootNodeType.Connection);
        RootNodeInfo second = new(RootNodeType.Connection);

        ConnectionFileMigration.Establish(first, Password("recovery"), true, FastIterations);
        ConnectionFileMigration.Establish(second, Password("recovery"), true, FastIterations);

        Assert.That(first.FileKey!.Bytes.SequenceEqual(second.FileKey!.Bytes), Is.False);
    }

    [Test]
    public void ASharedStoreIsMigratedWithNoMachineProtector()
    {
        RootNodeInfo root = new(RootNodeType.Connection);

        ConnectionFileMigration.Establish(root, Password("recovery"), includeMachineProtector: false, FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(root.KeyProtection!.HasMachineProtector, Is.False);
            Assert.That(root.KeyProtection.MachineSlots, Is.Empty);
        });
    }

    [Test]
    public void AnEmptyRecoveryPasswordIsRefusedAndLeavesTheStoreAlone()
    {
        // Not optional. A store with only a machine protector loses its whole backup history the
        // moment the profile is rebuilt, with no signal until the day a backup is needed.
        RootNodeInfo root = new(RootNodeType.Connection);

        Assert.Throws<ArgumentException>(
            () => ConnectionFileMigration.Establish(root, new SecureString(), true, FastIterations));

        Assert.Multiple(() =>
        {
            Assert.That(root.KeyProtection, Is.Null, "the store is untouched");
            Assert.That(root.FileKey, Is.Null);
        });
    }

    [Test]
    public void AStoreThatWasNeverMigratedIsNotReportedAsProtected()
    {
        Assert.That(ConnectionFileMigration.IsAlreadyProtected(new RootNodeInfo(RootNodeType.Connection)),
            Is.False);
    }

    [Test]
    public void ASharedStoreIsToldItWillNeedThePasswordEverywhere()
    {
        string shared = StorageFormatUpgrade.BuildRecoveryPasswordExplanation(
            storeMayBeShared: true, isPortableEdition: false);

        Assert.Multiple(() =>
        {
            Assert.That(shared, Does.Contain(Language.RecoveryPasswordWhy));
            Assert.That(shared, Does.Contain(Language.RecoveryPasswordSharedStore));
        });
    }

    [Test]
    public void APrivateStoreIsNotToldAboutSharing()
    {
        string ownProfile = StorageFormatUpgrade.BuildRecoveryPasswordExplanation(
            storeMayBeShared: false, isPortableEdition: false);

        Assert.That(ownProfile, Does.Not.Contain(Language.RecoveryPasswordSharedStore));
    }

    [Test]
    public void ThePortableEditionIsNotToldItsFileMightBeShared()
    {
        // Portable suppresses the machine protector for a different reason, and only one of the two
        // is about sharing. Telling a portable user their file might be shared with colleagues would
        // be a guess presented as a fact.
        string portable = StorageFormatUpgrade.BuildRecoveryPasswordExplanation(
            storeMayBeShared: true, isPortableEdition: true);

        Assert.That(portable, Does.Not.Contain(Language.RecoveryPasswordSharedStore));
    }

    [Test]
    public void GivingAnAlreadyHardenedStoreAKeyCountsAsAChange()
    {
        // Task 5.9. `Apply` answers "was the level raised", and for a store hardened before per-file
        // keys existed the answer is no — while a random key and two protectors have just been
        // created for it. Deciding from that alone tells the caller nothing happened, and the caller
        // is what saves: the protectors would live in memory and never reach the file, on precisely
        // the stores that most needed them.
        Assert.Multiple(() =>
        {
            Assert.That(StorageFormatUpgrade.ConfirmationChangedTheStore(
                levelWasRaised: false, wasAlreadyProtected: false), Is.True,
                "the level did not move and a key was established, which is the whole 5.9 case");
            Assert.That(StorageFormatUpgrade.ConfirmationChangedTheStore(
                levelWasRaised: true, wasAlreadyProtected: false), Is.True,
                "an ordinary classic migration");
            Assert.That(StorageFormatUpgrade.ConfirmationChangedTheStore(
                levelWasRaised: false, wasAlreadyProtected: true), Is.False,
                "nothing moved and nothing was created, so nothing needs writing");
        });
    }

    [Test]
    public void AStoreAlreadyAtTheHardenedLevelCanStillBeGivenAKey()
    {
        // The other half of 5.9, on the migration rather than on the decision. Nothing about
        // Establish depends on the level, and that is what makes the fix a change to the gate alone.
        RootNodeInfo root = new(RootNodeType.Connection) { StorageFormat = StorageFormatLevel.Hardened };

        ConnectionFileMigration.Establish(root, Password("recovery"), includeMachineProtector: false,
            iterations: FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(ConnectionFileMigration.IsAlreadyProtected(root));
            Assert.That(root.KeyProtection!.HasMachineProtector, Is.False);
            Assert.That(root.StorageFormat, Is.EqualTo(StorageFormatLevel.Hardened));
        });
    }

    [Test]
    public void DecliningIsToldPlainlyThatTheKeyIsPublishedInOurSource()
    {
        // Task 7.2, and the sentence this application has never said. A user who declines keeps a
        // file encrypted under mR3m and until now heard nothing back — and silence after a security
        // question reads as reassurance, which here is the exact opposite of the truth.
        string declined = StorageFormatUpgrade.BuildDeclineExplanation(
            recoveryPasswordDeclined: false, storeIsKeyedOnThePublishedDefault: true);

        Assert.Multiple(() =>
        {
            Assert.That(declined, Does.Contain(Language.StorageFormatDeclined));
            Assert.That(declined, Does.Contain(Language.StorageFormatDeclinedLegacyKey));
        });
    }

    [Test]
    public void DecliningOnlyTheRecoveryPasswordIsToldTheSameThing()
    {
        // The two ways to end up with an unhardened store differ in what the user thought they were
        // answering, and not at all in what they are left holding. Someone who accepted the format
        // and then declined the password may well believe they hardened it.
        string declined = StorageFormatUpgrade.BuildDeclineExplanation(
            recoveryPasswordDeclined: true, storeIsKeyedOnThePublishedDefault: true);

        Assert.Multiple(() =>
        {
            Assert.That(declined, Does.Contain(Language.RecoveryPasswordDeclined));
            Assert.That(declined, Does.Contain(Language.StorageFormatDeclinedLegacyKey));
        });
    }

    [Test]
    public void AStoreWithAMasterPasswordIsNotToldItsKeyIsPublished()
    {
        // Because it is not. A warning that turns out to be false is worth less than no warning, and
        // this one would be read by exactly the users who did take the existing advice.
        string declined = StorageFormatUpgrade.BuildDeclineExplanation(
            recoveryPasswordDeclined: false, storeIsKeyedOnThePublishedDefault: false);

        Assert.That(declined, Does.Not.Contain(Language.StorageFormatDeclinedLegacyKey));
    }

    [Test]
    public void WhetherTheKeyIsThePublishedOneIsReadFromTheStoreRatherThanAssumed()
    {
        RootNodeInfo legacy = new(RootNodeType.Connection);
        RootNodeInfo withMasterPassword = new(RootNodeType.Connection) { PasswordString = "hunter2" };

        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        RootNodeInfo hardened = new(RootNodeType.Connection)
        {
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, Password("recovery"), iterations: FastIterations)
        };

        Assert.Multiple(() =>
        {
            Assert.That(StorageFormatUpgrade.StoreIsKeyedOnThePublishedDefault(legacy),
                "no master password means mR3m, which is the case this whole change exists for");
            Assert.That(StorageFormatUpgrade.StoreIsKeyedOnThePublishedDefault(withMasterPassword), Is.False);

            // PasswordString still returns the default for a hardened store, so reading it alone
            // would call a store keyed on its own random key a store keyed on a published constant.
            Assert.That(StorageFormatUpgrade.StoreIsKeyedOnThePublishedDefault(hardened), Is.False);
        });
    }
}
