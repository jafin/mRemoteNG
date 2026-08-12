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
    public void AStoreOutsideTheProfileGetsTheRecoveryProtectorAlone(string storePath)
    {
        // The file carries one machine protector, so on a shared file it serves exactly one person
        // and costs everyone else a prompt they have no way to remove.
        Assert.That(MachineProtectorPolicy.ShouldWriteMachineProtector(storePath, isPortableEdition: false),
            Is.False);
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
        // The asymmetry that settles every unknowable case: being wrong this way costs one prompt on
        // a file that would not otherwise have prompted, and being wrong the other way costs every
        // other member of a team a prompt they can never remove.
        Assert.That(MachineProtectorPolicy.ShouldWriteMachineProtector(storePath, isPortableEdition: false),
            Is.False);
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
            Assert.That(root.KeyProtection.MachineProtector, Is.Null);
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
            willWriteMachineProtector: false, isPortableEdition: false);

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
            willWriteMachineProtector: true, isPortableEdition: false);

        Assert.That(ownProfile, Does.Not.Contain(Language.RecoveryPasswordSharedStore));
    }

    [Test]
    public void ThePortableEditionIsNotToldItsFileMightBeShared()
    {
        // Portable suppresses the machine protector for a different reason, and only one of the two
        // is about sharing. Telling a portable user their file might be shared with colleagues would
        // be a guess presented as a fact.
        string portable = StorageFormatUpgrade.BuildRecoveryPasswordExplanation(
            willWriteMachineProtector: false, isPortableEdition: true);

        Assert.That(portable, Does.Not.Contain(Language.RecoveryPasswordSharedStore));
    }
}
