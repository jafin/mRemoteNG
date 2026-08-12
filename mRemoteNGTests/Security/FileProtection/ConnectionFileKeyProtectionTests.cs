using System;
using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using System.Xml.Linq;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// The two protectors that wrap a connection file's key, and the rule for choosing between them.
/// </summary>
/// <remarks>
/// The property under test throughout is that either protector alone recovers the same key, and that
/// neither can produce a different one. A protector that failed by returning plausible bytes would be
/// worse than the legacy default key it replaces: the file would open, silently, onto nothing.
/// </remarks>
[TestFixture]
public class ConnectionFileKeyProtectionTests
{
    // The cost of the derivation is not what these tests are about, and 600,000 iterations of
    // PBKDF2-SHA256 per wrap would put seconds on the suite. The default is exercised once, in
    // TheDefaultIterationCountIsWhatTheProtectorRecords.
    private const int FastIterations = 1000;

    private static SecureString Password(string value) => value.ConvertToSecureString();

    private static Func<Optional<SecureString>> Supplies(string value, Action? onAsk = null) => () =>
    {
        onAsk?.Invoke();
        return Password(value);
    };

    private static Optional<SecureString> NeverAsked()
    {
        Assert.Fail("the machine protector should have opened this file without a prompt");
        return Optional<SecureString>.Empty;
    }

    [Test]
    public void EitherProtectorAloneRecoversTheSameKey()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("correct horse"), iterations: FastIterations);

        using ConnectionFileKey viaMachine = DpapiKeyProtector.Unwrap(protection.MachineProtector);
        using ConnectionFileKey viaPassword =
            RecoveryPasswordKeyProtector.Unwrap(protection.RecoveryProtector, Password("correct horse"));

        Assert.Multiple(() =>
        {
            Assert.That(viaMachine.Bytes.SequenceEqual(fileKey.Bytes), "the machine protector holds the file key");
            Assert.That(viaPassword.Bytes.SequenceEqual(fileKey.Bytes), "and so does the recovery protector");
        });
    }

    [Test]
    public void TheMachineProtectorIsTriedFirstAndNoPasswordIsAskedFor()
    {
        // The daily case, and the one that decides whether this design is acceptable at all: a user
        // who has migrated must not be typing a password at every launch.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        using ConnectionFileKey unwrapped = protection.Unwrap(NeverAsked);

        Assert.That(unwrapped.Bytes.SequenceEqual(fileKey.Bytes));
    }

    [Test]
    public void AFileWithOnlyThePasswordProtectorOpens()
    {
        // The portable edition writes exactly this, and so does every file that has travelled to a
        // machine whose DPAPI blob is useless. It must open on the recovery password alone.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        using ConnectionFileKey unwrapped = protection.Unwrap(Supplies("recovery"));

        Assert.Multiple(() =>
        {
            Assert.That(protection.HasMachineProtector, Is.False);
            Assert.That(protection.MachineProtector, Is.Null);
            Assert.That(unwrapped.Bytes.SequenceEqual(fileKey.Bytes));
        });
    }

    [Test]
    public void AnUnusableMachineProtectorFallsBackToThePasswordAndSaysWhy()
    {
        // A file restored on another machine. The prompt on its own reads as "your password is
        // wrong"; the caller is told the actual cause first so it can say so.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        ConnectionFileKeyProtection fromElsewhere = ConnectionFileKeyProtection.Read(
            ForeignMachineProtector(), protection.RecoveryProtector)!;

        KeyProtectionException? reported = null;
        using ConnectionFileKey unwrapped =
            fromElsewhere.Unwrap(Supplies("recovery"), ex => reported = ex);

        Assert.Multiple(() =>
        {
            Assert.That(unwrapped.Bytes.SequenceEqual(fileKey.Bytes));
            Assert.That(reported, Is.Not.Null, "the machine-protector failure is reported before anything is asked for");
            Assert.That(reported!.Protector, Is.EqualTo(KeyProtector.Machine));
        });
    }

    [Test]
    public void AWrongRecoveryPasswordFailsRatherThanProducingAKey()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        int prompts = 0;
        KeyProtectionException ex = Assert.Throws<KeyProtectionException>(
            () => protection.Unwrap(Supplies("wrong", () => prompts++)))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Protector, Is.EqualTo(KeyProtector.RecoveryPassword));
            Assert.That(prompts, Is.EqualTo(3), "the password is re-asked for, as the master password path does");
        });
    }

    [Test]
    public void DecliningToSupplyARecoveryPasswordStopsRatherThanLooping()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        int prompts = 0;

        Assert.Throws<KeyProtectionException>(() => protection.Unwrap(() =>
        {
            prompts++;
            return Optional<SecureString>.Empty;
        }));

        Assert.That(prompts, Is.EqualTo(1), "a cancelled prompt is an answer, not a retry");
    }

    [Test]
    public void ANonInteractiveCallerIsNotMadeToBlock()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        Assert.Throws<KeyProtectionException>(() => protection.Unwrap(null));
    }

    [Test]
    public void ChangingTheRecoveryPasswordLeavesTheFileKeyAndTheMachineProtectorAlone()
    {
        // Task 3.4. The file key is the same key, so nothing the file holds is re-encrypted — which
        // is also what makes a rolling backup taken before the change still openable on the machine
        // that wrote it.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection original =
            ConnectionFileKeyProtection.Create(fileKey, Password("first"), iterations: FastIterations);

        ConnectionFileKeyProtection replaced =
            original.WithRecoveryPassword(fileKey, Password("second"), iterations: FastIterations);

        using ConnectionFileKey viaNewPassword =
            RecoveryPasswordKeyProtector.Unwrap(replaced.RecoveryProtector, Password("second"));

        Assert.Multiple(() =>
        {
            Assert.That(viaNewPassword.Bytes.SequenceEqual(fileKey.Bytes), "the same file key comes back");
            Assert.That(replaced.MachineProtector, Is.EqualTo(original.MachineProtector),
                "the machine protector is untouched, so the daily path is unaffected");
            Assert.That(replaced.RecoveryProtector, Is.Not.EqualTo(original.RecoveryProtector));
            Assert.Throws<KeyProtectionException>(
                () => RecoveryPasswordKeyProtector.Unwrap(replaced.RecoveryProtector, Password("first")),
                "and the old password no longer opens it");
        });
    }

    [Test]
    public void APortableFileCanBeGivenAMachineProtectorWithoutLosingItsPassword()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection portable = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        ConnectionFileKeyProtection installed = portable.WithMachineProtector(fileKey);

        using ConnectionFileKey viaMachine = DpapiKeyProtector.Unwrap(installed.MachineProtector);
        using ConnectionFileKey viaPassword =
            RecoveryPasswordKeyProtector.Unwrap(installed.RecoveryProtector, Password("recovery"));

        Assert.Multiple(() =>
        {
            Assert.That(viaMachine.Bytes.SequenceEqual(fileKey.Bytes));
            Assert.That(viaPassword.Bytes.SequenceEqual(fileKey.Bytes));
            Assert.That(installed.RecoveryProtector, Is.EqualTo(portable.RecoveryProtector));
        });
    }

    [Test]
    public void TheTwoWrappedBlobsDifferFromEachOtherAndAcrossFiles()
    {
        using ConnectionFileKey first = ConnectionFileKey.Generate();
        using ConnectionFileKey second = ConnectionFileKey.Generate();

        ConnectionFileKeyProtection a =
            ConnectionFileKeyProtection.Create(first, Password("same password"), iterations: FastIterations);
        ConnectionFileKeyProtection b =
            ConnectionFileKeyProtection.Create(second, Password("same password"), iterations: FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(a.MachineProtector, Is.Not.EqualTo(a.RecoveryProtector));
            Assert.That(a.MachineProtector, Is.Not.EqualTo(b.MachineProtector));

            // The salt and nonce are random per wrap, so even one password across two files gives
            // two unrelated blobs. A constant here would let one file's protector be recognised in
            // another's, which is how a "protected" file turns out to be keyed on nothing.
            Assert.That(a.RecoveryProtector, Is.Not.EqualTo(b.RecoveryProtector));
            Assert.That(first.Bytes.SequenceEqual(second.Bytes), Is.False, "and the keys themselves are random");
        });
    }

    [Test]
    public void TheSameKeyAndPasswordWrapDifferentlyEveryTime()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        string first = RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), FastIterations);
        string second = RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), FastIterations);

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void AMachineProtectorWithNoRecoveryProtectorIsRefused()
    {
        // Not a file this application writes. Accepting it would produce a store that opens today and
        // can never be recovered — the failure the second protector exists to prevent.
        Assert.Throws<KeyProtectionException>(() => ConnectionFileKeyProtection.Read(ForeignMachineProtector(), null));
    }

    [Test]
    public void AFileWithNeitherProtectorIsNotAPerFileKeyFile()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConnectionFileKeyProtection.Read(null, null), Is.Null);
            Assert.That(ConnectionFileKeyProtection.Read("", "   "), Is.Null);
        });
    }

    [Test]
    public void TheProtectorsRoundTripThroughTheRootElement()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);

        XElement root = new("Connections");
        protection.WriteTo(root);

        ConnectionFileKeyProtection? read = ConnectionFileKeyProtection.Read(
            root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName)?.Value,
            root.Attribute(ConnectionFileKeyProtection.RecoveryProtectorAttributeName)?.Value);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.MachineProtector, Is.EqualTo(protection.MachineProtector));
            Assert.That(read.RecoveryProtector, Is.EqualTo(protection.RecoveryProtector));
        });
    }

    [Test]
    public void APortableFileWritesNoMachineAttributeAtAll()
    {
        // An empty attribute would be read back as a machine protector that fails, which turns every
        // portable open into a fallback with a message about another machine.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection portable = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        XElement root = new("Connections");
        portable.WriteTo(root);

        Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null);
    }

    [Test]
    public void TheDefaultIterationCountIsWhatTheProtectorRecords()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        string wrapped = RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"));
        byte[] blob = Convert.FromBase64String(wrapped);
        int iterations = (blob[2] << 24) | (blob[3] << 16) | (blob[4] << 8) | blob[5];

        Assert.Multiple(() =>
        {
            Assert.That(iterations, Is.EqualTo(RecoveryPasswordKeyProtector.DefaultIterations));
            Assert.That(RecoveryPasswordKeyProtector.DefaultIterations, Is.GreaterThanOrEqualTo(600_000));
        });
    }

    [Test]
    public void ARecoveryProtectorMustNotBeStretchedWithSha1()
    {
        // SHA-1 is the default everywhere else in this file format because fifteen years of files
        // used it. A recovery protector is written for the first time here and has no such history.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        Assert.Multiple(() =>
        {
            Assert.That(RecoveryPasswordKeyProtector.IsSupportedPrf(HashAlgorithmName.SHA1), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryPasswordKeyProtector.Wrap(
                fileKey, Password("recovery"), FastIterations, HashAlgorithmName.SHA1));
        });
    }

    [TestCase("SHA256")]
    [TestCase("SHA512")]
    public void EachSupportedDerivationRoundTrips(string prf)
    {
        HashAlgorithmName function = new(prf);

        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        string wrapped = RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), FastIterations, function);

        using ConnectionFileKey unwrapped = RecoveryPasswordKeyProtector.Unwrap(wrapped, Password("recovery"));

        Assert.That(unwrapped.Bytes.SequenceEqual(fileKey.Bytes));
    }

    [Test]
    public void TheRecordedParametersCannotBeWoundBackWithoutFailingTheTag()
    {
        // An attacker who could rewrite the iteration count in place would turn a 600,000-iteration
        // protector into a brute-forceable one and leave the file opening normally. It cannot be
        // done: the count is an input to the derivation, so an edited blob derives a different key
        // and fails the tag. It is authenticated as well, which is what covers the one header field
        // the derivation does not consume.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        byte[] blob = Convert.FromBase64String(
            RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), 200_000));

        blob[2] = 0;
        blob[3] = 0;
        blob[4] = 0x27;
        blob[5] = 0x10; // 10,000 — still above the floor, so the tag is what refuses it

        KeyProtectionException ex = Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(Convert.ToBase64String(blob), Password("recovery")))!;

        Assert.That(ex.Protector, Is.EqualTo(KeyProtector.RecoveryPassword));
    }

    [Test]
    public void ATruncatedOrForeignProtectorIsRefusedRatherThanGuessedAt()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<KeyProtectionException>(
                () => RecoveryPasswordKeyProtector.Unwrap("not base64 at all !!", Password("x")));
            Assert.Throws<KeyProtectionException>(
                () => RecoveryPasswordKeyProtector.Unwrap(Convert.ToBase64String(new byte[10]), Password("x")));
            Assert.Throws<KeyProtectionException>(() => DpapiKeyProtector.Unwrap("not base64 at all !!"));
            Assert.Throws<KeyProtectionException>(() => DpapiKeyProtector.Unwrap(null));
        });
    }

    [Test]
    public void WritingAPortableProtectionRemovesAMachineProtectorThatWasAlreadyThere()
    {
        // The worst possible leftover. A stale machine protector still unwraps — to the *previous*
        // file key — and Unwrap prefers it over the recovery protector, so the file would open with
        // no prompt onto contents that no longer decrypt. Reachable whenever an element that already
        // carried one is written by an instance that has none: a portable edition re-saving an
        // installed file, or a rekey.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection installed =
            ConnectionFileKeyProtection.Create(fileKey, Password("recovery"), iterations: FastIterations);
        ConnectionFileKeyProtection portable = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        XElement root = new("Connections");
        installed.WriteTo(root);
        portable.WriteTo(root);

        Assert.That(root.Attribute(ConnectionFileKeyProtection.MachineProtectorAttributeName), Is.Null,
            "the machine protector attribute is removed, not left behind");
    }

    [Test]
    public void AnUnusableRecoveryProtectorIsNotAskedAboutThreeTimes()
    {
        // A truncated or foreign protector is a property of the file, not of the password. Burning
        // three prompts on it teaches the user their password is wrong when no password opens it —
        // the same misdiagnosis the storage-format refusal exists to avoid.
        ConnectionFileKeyProtection protection =
            ConnectionFileKeyProtection.Read(null, Convert.ToBase64String(new byte[10]))!;

        int prompts = 0;
        KeyProtectionException ex = Assert.Throws<KeyProtectionException>(
            () => protection.Unwrap(Supplies("anything", () => prompts++)))!;

        Assert.Multiple(() =>
        {
            Assert.That(prompts, Is.EqualTo(1), "asked once, then stopped");
            Assert.That(ex.Failure, Is.EqualTo(KeyProtectionFailure.Unusable));
            Assert.That(ex.IsRetryable, Is.False);
        });
    }

    [Test]
    public void AWrongPasswordIsRetryableAndAnUnreadableProtectorIsNot()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        ConnectionFileKeyProtection protection = ConnectionFileKeyProtection.Create(
            fileKey, Password("recovery"), includeMachineProtector: false, iterations: FastIterations);

        KeyProtectionException wrongPassword = Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(protection.RecoveryProtector, Password("wrong")))!;
        KeyProtectionException unreadable = Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(Convert.ToBase64String(new byte[10]), Password("x")))!;

        Assert.Multiple(() =>
        {
            Assert.That(wrongPassword.Failure, Is.EqualTo(KeyProtectionFailure.WrongSecret));
            Assert.That(unreadable.Failure, Is.EqualTo(KeyProtectionFailure.Unusable));
        });
    }

    [Test]
    public void AnIterationCountOutsideTheWrittenRangeIsRefusedBeforeAnythingIsDerived()
    {
        // The count decides how long the derivation runs, and the tag that would expose it as forged
        // cannot be checked until the derivation has finished. Without a ceiling a hostile file makes
        // opening it take hours, which presents as the application having hung.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        byte[] blob = Convert.FromBase64String(
            RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), FastIterations));

        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(2, 4), int.MaxValue);

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        KeyProtectionException ex = Assert.Throws<KeyProtectionException>(
            () => RecoveryPasswordKeyProtector.Unwrap(Convert.ToBase64String(blob), Password("recovery")))!;
        clock.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(ex.Failure, Is.EqualTo(KeyProtectionFailure.Unusable));
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(1000),
                "refused without deriving — the point of the ceiling");
        });
    }

    [Test]
    public void WrapRefusesACountItsOwnUnwrapWouldReject()
    {
        // Otherwise a caller can write a protector that can never be opened, and if the machine
        // protector is later lost the file key is gone with it.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RecoveryPasswordKeyProtector.Wrap(fileKey, Password("recovery"), 999));
            Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryPasswordKeyProtector.Wrap(
                fileKey, Password("recovery"), RecoveryPasswordKeyProtector.MaximumIterations + 1));
            Assert.That(RecoveryPasswordKeyProtector.DefaultIterations,
                Is.InRange(RecoveryPasswordKeyProtector.MinimumIterations,
                           RecoveryPasswordKeyProtector.MaximumIterations));
        });
    }

    [Test]
    public void TheProtectionScopeIsAWriteTimePropertyThatUnwrappingCannotCheck()
    {
        // Pinned because it is surprising and because the obvious defensive check does nothing.
        //
        // The scope is carried inside the blob and Win32's CryptUnprotectData takes no scope
        // argument, so the DataProtectionScope passed to Unprotect is ignored: a LocalMachine blob
        // unwraps perfectly through a CurrentUser call. Choosing CurrentUser therefore binds what
        // *this* build writes; it is not something the reader can enforce, and adding a check that
        // appears to enforce it would be worse than the honest absence of one.
        //
        // That also fixes the limit of what this fixture can cover. Every test here runs as one
        // account on one machine, where a LocalMachine-scoped protector behaves identically to a
        // CurrentUser one. Only a second account tells them apart, which is manual task 8.5.
        byte[] machineScoped = ProtectedData.Protect(
            new byte[ConnectionFileKey.SizeInBytes],
            "mRemoteNG.ConnectionFileKey.v1"u8.ToArray(),
            DataProtectionScope.LocalMachine);

        using ConnectionFileKey unwrapped = DpapiKeyProtector.Unwrap(Convert.ToBase64String(machineScoped));

        Assert.That(unwrapped.Bytes.SequenceEqual(new byte[ConnectionFileKey.SizeInBytes]),
            "the scope argument on Unprotect is inert — see the comment above before adding a check for it");
    }

    [Test]
    public void ADisposedKeyCannotBeReadAgain()
    {
        ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        fileKey.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = fileKey.Bytes.Length;
        });
    }

    /// <summary>
    /// A DPAPI blob this account cannot unprotect, standing in for a file that arrived from
    /// somewhere else. Built with entropy the protector does not use, which produces the same
    /// failure a foreign blob does — it stands in for the scope rather than demonstrating it.
    /// Demonstrating it is not possible from one account: see
    /// <see cref="TheProtectionScopeIsAWriteTimePropertyThatUnwrappingCannotCheck"/>, and manual
    /// task 8.5 for the coverage that needs a second account.
    /// </summary>
    private static string ForeignMachineProtector() =>
        Convert.ToBase64String(ProtectedData.Protect(
            new byte[ConnectionFileKey.SizeInBytes], "some other application"u8.ToArray(),
            DataProtectionScope.CurrentUser));
}
