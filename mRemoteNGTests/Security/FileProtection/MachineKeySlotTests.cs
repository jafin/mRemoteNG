using System;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Xml.Linq;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// The machine protector as a set of slots: one per member of a shared connection file.
/// </summary>
/// <remarks>
/// <para>
/// One machine protector serves one Windows account, so on a file a team shares it served whoever
/// migrated it and cost every other member the recovery password on every open, with no way to make
/// that stop. That is why the preceding change wrote <i>no</i> machine protector at all for a file
/// outside the user profile: with one slot, none was the only defensible number.
/// </para>
/// <para>
/// The property throughout is that a slot list behaves exactly as the single value did when it holds
/// one entry — same bytes on disk, same reader — and that finding a slot never accepts a key that is
/// not this file's. A DPAPI unwrap succeeding proves the blob belongs to this account, not that it
/// belongs to this file.
/// </para>
/// </remarks>
[TestFixture]
public class MachineKeySlotTests
{
    private const int FastIterations = 1000;

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static Func<Optional<SecureString>> Supplies(string value) => () => Password(value);

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("a slot on this file should have opened it without a prompt");
        return Optional<SecureString>.Empty;
    }

    /// <summary>A DPAPI blob this account can unwrap that holds someone else's key entirely.</summary>
    private static string SlotForAnotherKey()
    {
        using ConnectionFileKey other = ConnectionFileKey.Generate();
        return DpapiKeyProtector.Wrap(other);
    }

    /// <summary>The validator the readers use: only this file's key is accepted.</summary>
    private static Func<ConnectionFileKey, bool> Only(ConnectionFileKey expected) =>
        candidate => candidate.Bytes.SequenceEqual(expected.Bytes);

    [SetUp]
    public void ForgetTheSessionSlot() => MachineSlotSession.Forget();

    [Test]
    public void OneSlotIsExactlyWhatTheSingleValueFormWas()
    {
        // The compatibility claim the whole design rests on: a file with one member is byte-identical
        // to what the preceding change wrote, so there is no old form, no new form, and nothing to
        // migrate. If this ever fails, every file written before slots existed needs a migration.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        XElement root = new("Connections");
        protection.WriteTo(root);
        string written = root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(protection.MachineSlots, Has.Count.EqualTo(1));
            Assert.That(written, Is.EqualTo(protection.MachineSlots[0]));
            Assert.That(written, Does.Not.Contain(ConnectionFileKeyProtection.SlotSeparator));
        });
    }

    [Test]
    public void AnAbsentAttributeStillMeansNoMachineProtector()
    {
        // The portable edition, and any file written outside the user profile before slots existed.
        ConnectionFileKeyProtection portable = ConnectionFileKeyProtection.Create(
            ConnectionFileKey.Generate(), Password("recovery"), includeMachineProtector: false,
            iterations: FastIterations);

        ConnectionFileKeyProtection read =
            ConnectionFileKeyProtection.Read(null, portable.RecoveryProtector)!;

        Assert.Multiple(() =>
        {
            Assert.That(read.HasMachineProtector, Is.False);
            Assert.That(read.MachineSlots, Is.Empty);
        });
    }

    [Test]
    public void EverySlotUnwrapsToTheSameFileKey()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations)
                .WithMachineProtector(fileKey)
                .WithMachineProtector(fileKey);

        Assert.That(protection.MachineSlots, Has.Count.EqualTo(3));

        foreach (string slot in protection.MachineSlots)
        {
            using ConnectionFileKey fromSlot = DpapiKeyProtector.Unwrap(slot);
            Assert.That(fromSlot.Bytes.SequenceEqual(fileKey.Bytes), "every slot holds this file's key");
        }
    }

    [Test]
    public void AddingASlotKeepsTheOnesAlreadyThere()
    {
        // Additive because the slots already in the file belong to other people. Replacing them would
        // make a shared file serve one member at a time, each save locking out whoever saved before.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection first =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        ConnectionFileKeyProtection second = first.WithMachineProtector(fileKey);

        Assert.That(second.MachineSlots[0], Is.EqualTo(first.MachineSlots[0]));
    }

    [Test]
    public void AForeignSlotDoesNotStopTheValidOneBeingFound()
    {
        // The ordinary case on a shared file: most slots belong to other accounts, DPAPI refuses them
        // quickly, and the search carries on to the one that is ours.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        string list = string.Join(ConnectionFileKeyProtection.SlotSeparator,
            ForeignSlot(), mine.MachineSlots[0], ForeignSlot());

        ConnectionFileKeyProtection shared =
            ConnectionFileKeyProtection.Read(list, mine.RecoveryProtector)!;

        using ConnectionFileKey opened = shared.Unwrap(NeverAsked, keyValidator: Only(fileKey));

        Assert.That(opened.Bytes.SequenceEqual(fileKey.Bytes));
    }

    [Test]
    public void ASlotHoldingAnotherFilesKeyIsRejectedAndTheSearchContinues()
    {
        // This is the case an unwrap-and-accept search gets wrong. A slot copied from another file
        // the same account owns unwraps perfectly and yields the wrong key; taking it opens the store
        // onto contents that do not decrypt, with no prompt and nothing reported.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        string list = string.Join(ConnectionFileKeyProtection.SlotSeparator,
            SlotForAnotherKey(), mine.MachineSlots[0]);

        ConnectionFileKeyProtection shared =
            ConnectionFileKeyProtection.Read(list, mine.RecoveryProtector)!;

        using ConnectionFileKey opened = shared.Unwrap(NeverAsked, keyValidator: Only(fileKey));

        Assert.That(opened.Bytes.SequenceEqual(fileKey.Bytes), "the wrong-key slot was skipped, not taken");
    }

    [Test]
    public void WhenNoSlotBelongsToThisFileTheRecoveryPasswordIsAskedForOnce()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        string list = string.Join(ConnectionFileKeyProtection.SlotSeparator,
            SlotForAnotherKey(), ForeignSlot(), SlotForAnotherKey());

        ConnectionFileKeyProtection shared = ConnectionFileKeyProtection.Read(list, mine.RecoveryProtector)!;

        int reports = 0;
        using ConnectionFileKey opened =
            shared.Unwrap(Supplies("recovery"), _ => reports++, Only(fileKey));

        Assert.Multiple(() =>
        {
            Assert.That(opened.Bytes.SequenceEqual(fileKey.Bytes));

            // Once for the list, not once per slot. On a shared file most slots are expected to fail
            // — they belong to other people — and a warning per member would turn a normal open into
            // a wall of alarming text.
            Assert.That(reports, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnAttributeThatIsPresentAndEmptyIsRefused()
    {
        // Not the same as an absent attribute. Nothing this application writes produces it, so
        // reading it as "no machine protector" would accept a file something else has damaged and
        // then prompt for the recovery password as though that were normal.
        ConnectionFileKeyProtection any = ConnectionFileKeyProtection.Create(
            ConnectionFileKey.Generate(), Password("recovery"), iterations: FastIterations);

        Assert.Multiple(() =>
        {
            Assert.Throws<KeyProtectionException>(
                () => ConnectionFileKeyProtection.Read("", any.RecoveryProtector));
            Assert.Throws<KeyProtectionException>(
                () => ConnectionFileKeyProtection.Read("   ", any.RecoveryProtector));
            Assert.Throws<KeyProtectionException>(
                () => ConnectionFileKeyProtection.Read($"{any.MachineSlots[0]}||{ForeignSlot()}",
                    any.RecoveryProtector),
                "an empty entry inside the list is the same kind of damage");
        });
    }

    [Test]
    public void AnOverLongListIsRefusedWithoutTryingASlot()
    {
        // Refused on its count rather than on its hundred-thousandth failure: each slot costs a DPAPI
        // call, so an unbounded list is a file that takes minutes to refuse to open. One real slot is
        // placed first, so a reader that tried slots before counting them would succeed and fail this.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        string tooMany = string.Join(ConnectionFileKeyProtection.SlotSeparator,
            Enumerable.Repeat(mine.MachineSlots[0], ConnectionFileKeyProtection.MaxMachineSlots + 1));

        KeyProtectionException thrown = Assert.Throws<KeyProtectionException>(
            () => ConnectionFileKeyProtection.Read(tooMany, mine.RecoveryProtector))!;

        Assert.That(thrown.Protector, Is.EqualTo(KeyProtector.Machine));
    }

    [Test]
    public void ASlotCannotBeAddedBeyondTheBound()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        while (protection.MachineSlots.Count < ConnectionFileKeyProtection.MaxMachineSlots)
            protection = protection.WithMachineProtector(fileKey);

        Assert.Throws<KeyProtectionException>(() => protection.WithMachineProtector(fileKey));
    }

    [Test]
    public void AMultiSlotValueIsNotSilentlyReadAsOneBlobByASingleSlotReader()
    {
        // The reason the separator is `|` and not a space: Convert.FromBase64String ignores
        // whitespace, so a space-separated list would sometimes concatenate into a longer valid blob
        // instead of failing, depending on whether the entries carry `=` padding. An older build must
        // fail the same way every time, and then fall through to the recovery password — which is
        // exactly what it did before slots existed.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations)
                .WithMachineProtector(fileKey);

        XElement root = new("Connections");
        protection.WriteTo(root);
        string written = root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain(ConnectionFileKeyProtection.SlotSeparator));

            // What the older build does with it: one Convert.FromBase64String on the whole value.
            Assert.Throws<FormatException>(() => Convert.FromBase64String(written));
        });
    }

    [Test]
    public void TheSlotThatOpenedTheStoreIsTriedFirstNextTime()
    {
        // Slots are unlabelled, so the only way to find one is to try them. Remembering the one that
        // worked is what stops the member listed last paying for everyone ahead of them on every
        // read — a store is read more than once per run.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        string list = string.Join(ConnectionFileKeyProtection.SlotSeparator,
            ForeignSlot(), ForeignSlot(), mine.MachineSlots[0]);

        ConnectionFileKeyProtection shared = ConnectionFileKeyProtection.Read(list, mine.RecoveryProtector)!;

        using (ConnectionFileKey first = shared.Unwrap(NeverAsked, keyValidator: Only(fileKey)))
            Assert.That(first.Bytes.SequenceEqual(fileKey.Bytes));

        Assert.That(MachineSlotSession.Peek(), Is.EqualTo(mine.MachineSlots[0]));

        // And a remembered slot is still validated rather than trusted: the same read against a file
        // whose slots are all foreign must not accept it.
        ConnectionFileKeyProtection elsewhere = ConnectionFileKeyProtection.Read(
            string.Join(ConnectionFileKeyProtection.SlotSeparator, ForeignSlot(), SlotForAnotherKey()),
            mine.RecoveryProtector)!;

        using ConnectionFileKey viaPassword =
            elsewhere.Unwrap(Supplies("recovery"), keyValidator: Only(fileKey));

        Assert.That(viaPassword.Bytes.SequenceEqual(fileKey.Bytes));
    }

    [Test]
    public void AMemberWhoOpenedWithTheRecoveryPasswordIsKnownToHaveNoSlot()
    {
        // What the saver tests. "Does the file have a machine protector" is the wrong question once
        // slots exist: a shared file has several and none of them is yours, which is precisely the
        // case that needs one added.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        ConnectionFileKeyProtection theirs = ConnectionFileKeyProtection.Read(
            string.Join(ConnectionFileKeyProtection.SlotSeparator, SlotForAnotherKey(), ForeignSlot()),
            mine.RecoveryProtector)!;

        using ConnectionFileKey opened = theirs.Unwrap(Supplies("recovery"), keyValidator: Only(fileKey));

        Assert.Multiple(() =>
        {
            Assert.That(theirs.HasMachineProtector, Is.True, "the file carries other members' slots");
            Assert.That(theirs.HasSlotForThisAccount, Is.False, "and none of them is this account's");
        });
    }

    [Test]
    public void AMemberWhoOpenedWithASlotIsNotGivenASecondOne()
    {
        // The other half: a member who already has a slot must not collect another on every save, or
        // a file used daily grows a slot a day until it hits the bound.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        ConnectionFileKeyProtection reopened =
            ConnectionFileKeyProtection.Read(mine.MachineSlots[0], mine.RecoveryProtector)!;

        using ConnectionFileKey opened = reopened.Unwrap(NeverAsked, keyValidator: Only(fileKey));

        Assert.That(reopened.HasSlotForThisAccount, Is.True);
    }

    [Test]
    public void AFreshlyProtectedStoreAlreadyHasThisAccountsSlot()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        ConnectionFileKeyProtection installed =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);
        ConnectionFileKeyProtection portable = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(installed.HasSlotForThisAccount, Is.True,
                "migration just wrote it, so the next save must not write a duplicate");
            Assert.That(portable.HasSlotForThisAccount, Is.False);
        });
    }

    [Test]
    public void ReadingAFileDoesNotAddASlotToIt()
    {
        // On save, never on open. Rewriting a file that was only read turns an inspection into a
        // permanent change, and on a shared file it would make every open a write to a file other
        // people have open.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection mine = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        ConnectionFileKeyProtection asRead =
            ConnectionFileKeyProtection.Read(ForeignSlot(), mine.RecoveryProtector)!;
        int slotsBefore = asRead.MachineSlots.Count;

        using ConnectionFileKey opened = asRead.Unwrap(Supplies("recovery"), keyValidator: Only(fileKey));

        XElement root = new("Connections");
        asRead.WriteTo(root);

        Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)!.Value
                .Split(ConnectionFileKeyProtection.SlotSeparator), Has.Length.EqualTo(slotsBefore),
            "opening the file left its slot list exactly as it was");
    }

    /// <summary>A DPAPI blob belonging to another application entirely — another member's slot.</summary>
    private static string ForeignSlot() =>
        Convert.ToBase64String(ProtectedData.Protect(
            new byte[ConnectionFileKey.SizeInBytes], "some other application"u8.ToArray(),
            DataProtectionScope.CurrentUser));
}
