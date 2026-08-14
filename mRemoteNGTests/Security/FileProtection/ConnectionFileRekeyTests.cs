using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// Rekeying: the only operation that actually removes a member's access.
/// </summary>
/// <remarks>
/// Deleting one member's slot does not remove their access — they know the recovery password and
/// have had the file — so slot deletion is not offered and this is. What a rekey cannot do is reach
/// a copy already taken, which is why the confirmation says so and why these tests assert the
/// wording as well as the behaviour.
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class ConnectionFileRekeyTests
{
    private const int FastIterations = 1000;

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static Func<ConnectionFileKey, bool> Only(ConnectionFileKey expected) =>
        candidate => candidate.Bytes.SequenceEqual(expected.Bytes);

    private static RootNodeInfo ProtectedRoot(string recoveryPassword, bool machineProtector = true)
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        return new RootNodeInfo(RootNodeType.Connection)
        {
            StorageFormat = StorageFormatLevel.Hardened,
            FileKey = ConnectionFileKey.FromBytes(fileKey.Bytes),
            KeyProtection = ConnectionFileKeyProtection.Create(
                fileKey, Password(recoveryPassword), machineProtector, FastIterations)
        };
    }

    [Test]
    public void TheOldRecoveryPasswordOpensNothingAfterwards()
    {
        RootNodeInfo root = ProtectedRoot("old password");

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: true,
                                  FastIterations);

        Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(root.KeyProtection!.RecoveryProtector,
                                                      Password("old password")));
    }

    [Test]
    public void TheNewRecoveryPasswordOpensTheNewKey()
    {
        RootNodeInfo root = ProtectedRoot("old password");

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: true,
                                  FastIterations);

        using ConnectionFileKey unwrapped =
            RecoveryPasswordKeyProtector.Unwrap(root.KeyProtection!.RecoveryProtector, Password("new password"));

        Assert.That(unwrapped.Bytes.SequenceEqual(root.FileKey!.Bytes));
    }

    [Test]
    public void TheKeyItselfIsReplaced()
    {
        // Not merely re-wrapped. A rekey that kept the key would leave every copy of the file
        // openable by anyone holding the old one, which is the whole thing it exists to stop.
        RootNodeInfo root = ProtectedRoot("old password");
        byte[] before = [.. root.FileKey!.Bytes];

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: true,
                                  FastIterations);

        Assert.That(root.FileKey!.Bytes.SequenceEqual(before), Is.False);
    }

    [Test]
    public void EverySlotIsDroppedAndOnlyThisAccountsIsWritten()
    {
        RootNodeInfo root = ProtectedRoot("old password");
        root.KeyProtection = root.KeyProtection!
            .WithMachineProtector(root.FileKey!)
            .WithMachineProtector(root.FileKey!);
        Assert.That(root.KeyProtection.MachineSlots, Has.Count.EqualTo(3), "three members before");

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: true,
                                  FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(root.KeyProtection!.MachineSlots, Has.Count.EqualTo(1),
                "the departed members' slots are gone");
            Assert.That(root.KeyProtection.HasSlotForThisAccount, Is.True,
                "and the account doing the rekey keeps its silent open");
        });
    }

    [Test]
    public void TheRemainingMembersOpenOnTheNewPasswordAndAreSlottedAgainOnSave()
    {
        // What a colleague meets after a rekey: the first open asks for the new password, exactly as
        // their first open of the file did, and their slot comes back when they next save.
        RootNodeInfo root = ProtectedRoot("old password", machineProtector: false);

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: false,
                                  FastIterations);

        ConnectionFileKeyProtection asTheySeeIt = ConnectionFileKeyProtection.Read(
            null, root.KeyProtection!.RecoveryProtector)!;

        using ConnectionFileKey opened =
            asTheySeeIt.Unwrap(() => Password("new password"), keyValidator: Only(root.FileKey!));

        Assert.Multiple(() =>
        {
            Assert.That(opened.Bytes.SequenceEqual(root.FileKey!.Bytes));
            Assert.That(asTheySeeIt.HasSlotForThisAccount, Is.False, "so the next save earns one");
        });
    }

    [Test]
    public void APortableRekeyWritesNoSlot()
    {
        // Rekeying is about the key and the password. It is not an occasion to start binding the
        // file to whichever machine the build was carried to.
        RootNodeInfo root = ProtectedRoot("old password", machineProtector: false);

        ConnectionFileRekey.Apply(root, Password("new password"), includeMachineProtector: false,
                                  FastIterations);

        Assert.That(root.KeyProtection!.MachineSlots, Is.Empty);
    }

    [Test]
    public void AStoreWithNoKeyOfItsOwnHasNothingToRekey()
    {
        RootNodeInfo classic = new(RootNodeType.Connection);

        Assert.Throws<ArgumentException>(
            () => ConnectionFileRekey.Apply(classic, Password("new password"), includeMachineProtector: true,
                                            FastIterations));
    }

    [Test]
    public void ARekeyWithNoNewPasswordIsRefusedAndChangesNothing()
    {
        // The last moment at which "a store nobody can open anywhere else" is still preventable.
        RootNodeInfo root = ProtectedRoot("old password");
        byte[] before = [.. root.FileKey!.Bytes];

        Assert.Throws<ArgumentException>(
            () => ConnectionFileRekey.Apply(root, Password(""), includeMachineProtector: true, FastIterations));

        Assert.That(root.FileKey!.Bytes.SequenceEqual(before), "the store is untouched");
    }

    [Test]
    public void TheConfirmationSaysWhatARekeyCannotDo()
    {
        // The part a user will not think of for themselves, and the reason slot deletion is not
        // offered: someone who kept a copy still has everything the file held up to that moment.
        string explanation = ConnectionFileRekeyPrompt.BuildExplanation();

        Assert.Multiple(() =>
        {
            Assert.That(explanation, Does.Contain(mRemoteNG.Resources.Language.Language.RekeyWhat));
            Assert.That(explanation, Does.Contain(mRemoteNG.Resources.Language.Language.RekeyLimit));
            Assert.That(explanation, Does.Contain(mRemoteNG.Resources.Language.Language.RekeyBackup));
        });
    }
}
