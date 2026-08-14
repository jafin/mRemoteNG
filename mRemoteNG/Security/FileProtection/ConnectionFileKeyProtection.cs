using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Xml.Linq;
using mRemoteNG.Tools;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// The two wrapped copies of a connection file's key, and the rule for choosing between them.
/// </summary>
/// <remarks>
/// <para>
/// A random per-file key encrypts the contents. That key is stored twice in the file's root: once
/// wrapped by DPAPI at <c>CurrentUser</c> scope, once wrapped by a key stretched from a recovery
/// password. Either unwraps it, which is what makes the rest of the design work — daily use costs no
/// prompt, and a file that has left the machine is still openable by the person who owns it.
/// </para>
/// <para>
/// <b>The recovery protector is not optional.</b> A file with only the machine protector is a file
/// whose entire backup history becomes worthless the moment the profile is rebuilt, with no signal
/// until the day it is needed. The portable edition is the one case that omits the <i>machine</i>
/// protector — see <c>Runtime.IsPortableEdition</c> and task 7.1 — and never the other way round.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ConnectionFileKeyProtection
{
    /// <summary>
    /// Root attribute holding the DPAPI-wrapped file keys, one per slot. Absent in the portable
    /// edition.
    /// </summary>
    public const string MachineProtectorAttributeName = "KeyProtectorMachine";

    /// <summary>Root attribute holding the recovery-password-wrapped file key. Always present.</summary>
    public const string RecoveryProtectorAttributeName = "KeyProtectorRecovery";

    /// <summary>
    /// Root attribute naming which key the file is on. Absent in files written before it existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written in the clear, and it says nothing: it is a random token whose only property is being
    /// different from the last one. What it is for is a save from a session that has not seen the
    /// rekey — the writer compares the file's token against the one it read, and refuses rather than
    /// writing the contents back under the key and the recovery password the rekey removed.
    /// </para>
    /// <para>
    /// <b>A token rather than a counter.</b> Two rekeys from two sessions each bump a counter to the
    /// same number, so a counter can say "unchanged" about two different keys — which is the one
    /// answer this must never give. Random tokens collide only by accident of 128 bits.
    /// </para>
    /// </remarks>
    public const string GenerationAttributeName = "KeyGeneration";

    /// <summary>
    /// What separates one slot from the next.
    /// </summary>
    /// <remarks>
    /// <b>Not a space.</b> <see cref="Convert.FromBase64String"/> ignores whitespace, so a
    /// space-separated list read by a build that expects a single blob would sometimes concatenate
    /// silently into a longer blob instead of failing — the outcome depending on whether the entries
    /// happen to carry <c>=</c> padding. <c>|</c> is outside the base64 alphabet and is not
    /// whitespace, so an older build fails the same way every time. Which exception it throws matters
    /// less than that it always throws one.
    /// </remarks>
    public const char SlotSeparator = '|';

    /// <summary>
    /// The most slots a file may declare.
    /// </summary>
    /// <remarks>
    /// Slots are tried in turn and each attempt costs a DPAPI call, so an unbounded list is a file
    /// that takes minutes to refuse to open. The bound is checked before any slot is tried, which is
    /// the part that matters — a file declaring a hundred thousand slots is rejected on its count
    /// rather than on its hundred-thousandth failure. Generous for its purpose: a slot is one member
    /// of a team sharing one file.
    /// </remarks>
    public const int MaxMachineSlots = 32;

    /// <summary>How many times a recovery password may be re-entered, matching <c>PasswordAuthenticator</c>.</summary>
    private const int MaxRecoveryPasswordAttempts = 3;

    private readonly string[] _machineSlots;

    private ConnectionFileKeyProtection(string[] machineSlots, string recoveryProtector,
                                        string? generation, bool hasSlotForThisAccount = false)
    {
        _machineSlots = machineSlots;
        RecoveryProtector = recoveryProtector;
        Generation = generation;
        HasSlotForThisAccount = hasSlotForThisAccount;
    }

    /// <summary>
    /// Whether one of the slots is known to belong to the account running now — either because this
    /// object just wrote one, or because one of them opened the file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the same question as <see cref="HasMachineProtector"/>, and the difference is the whole
    /// point of slots. A shared file carries other people's slots: it has machine protectors, and
    /// none of them is yours, so you were asked for the recovery password. This is what the saver
    /// tests to decide whether to earn you one.
    /// </para>
    /// <para>
    /// False is the safe direction. Being wrong that way costs a duplicate slot on one file — a few
    /// hundred bytes, and the count is bounded. Being wrong the other way leaves a member prompted
    /// for the recovery password on every open with no way to fix it, which is the defect this change
    /// exists to remove.
    /// </para>
    /// </remarks>
    public bool HasSlotForThisAccount { get; private set; }

    /// <summary>
    /// The wrapped copies of the file key that this machine might be able to open — one per member
    /// of a shared file, in the order the file lists them. Empty when the file carries none.
    /// </summary>
    /// <remarks>
    /// Unlabelled, and deliberately: nothing records whose slot is whose. Labelling would put an
    /// account identifier for every member of a team into a file that is on a share by definition,
    /// to support removing one slot — which does not remove anyone's access, because they know the
    /// recovery password and have had the file. Rekeying is the operation that does.
    /// </remarks>
    public IReadOnlyList<string> MachineSlots => _machineSlots;

    public string RecoveryProtector { get; }

    /// <summary>
    /// Which key this store is on. Null for a file written before generations existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Changed by <see cref="Create"/> alone, which means by hardening and by a rekey — never by
    /// adding a slot, changing the recovery password, or an ordinary save. It has to track the key
    /// rather than the file, because the thing it protects against is contents written under a key
    /// the file no longer uses.
    /// </para>
    /// <para>
    /// Null propagates rather than being filled in. A file that predates this gains a generation when
    /// it is next rekeyed and not before: adopting one on an ordinary save would give every other
    /// session holding that file a token they had never seen, and their next save — an ordinary save,
    /// which is meant to be last-writer-wins — would be refused for a rekey that never happened.
    /// </para>
    /// </remarks>
    public string? Generation { get; }

    public bool HasMachineProtector => _machineSlots.Length > 0;

    /// <param name="includeMachineProtector">
    /// False for the portable edition, which runs as whatever account happens to be at the keyboard
    /// and whose whole purpose is that its files open elsewhere. A DPAPI blob there would be dead
    /// weight at best and a file that opens on exactly one machine at worst.
    /// </param>
    public static ConnectionFileKeyProtection Create(ConnectionFileKey fileKey,
                                                     SecureString recoveryPassword,
                                                     bool includeMachineProtector = true,
                                                     int iterations = RecoveryPasswordKeyProtector.DefaultIterations,
                                                     HashAlgorithmName? prf = null)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        return new ConnectionFileKeyProtection(
            includeMachineProtector ? [DpapiKeyProtector.Wrap(fileKey)] : [],
            RecoveryPasswordKeyProtector.Wrap(fileKey, recoveryPassword, iterations, prf),
            NewGeneration(),
            hasSlotForThisAccount: includeMachineProtector);
    }

    /// <summary>
    /// A token for a key that has just been made. Never derived from the key: the generation is
    /// written in the clear beside the protectors, so anything computed from key material would be
    /// key material published.
    /// </summary>
    private static string NewGeneration() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Recovers the file key, trying the machine protector first and asking for the recovery password
    /// only if it cannot help.
    /// </summary>
    /// <param name="recoveryPasswordRequestor">
    /// Asked for the recovery password, once per attempt. A null requestor, or one that returns
    /// nothing, ends the attempt — this is the same contract the master-password path already uses,
    /// so a non-interactive caller cannot be made to block.
    /// <para>
    /// <b>Nothing here takes ownership of what it returns.</b> A password is derived from and
    /// discarded within the attempt that used it, so the caller is free to dispose every password it
    /// supplied once this returns or throws — and should, since it is the only thing that knows how
    /// many it handed over.
    /// </para>
    /// </param>
    /// <param name="onMachineProtectorFailed">
    /// Told why the machine protector could not be used, before the user is asked for anything. The
    /// prompt on its own reads as "your password is wrong"; the cause — this file was protected by
    /// another account or another machine — is the part that lets someone act on it.
    /// </param>
    /// <param name="keyValidator">
    /// Decides whether an unwrapped key is <i>this file's</i> key. A protector unwrapping proves only
    /// that this reader may use it: a machine protector left over from an earlier key, or copied from
    /// another file the same account owns, unwraps perfectly and yields a different key. Without this
    /// the store opens onto contents that cannot be decrypted, with no prompt and nothing reported.
    /// The caller supplies it because only the caller holds the file's protection declaration, which
    /// is the one ciphertext whose plaintext is known in advance.
    /// </param>
    /// <exception cref="KeyProtectionException">
    /// Neither protector produced the key. Neither can produce a <i>wrong</i> key: DPAPI and AES-GCM
    /// both authenticate, so failure is an exception rather than plausible bytes.
    /// </exception>
    public ConnectionFileKey Unwrap(Func<Optional<SecureString>>? recoveryPasswordRequestor,
                                    Action<KeyProtectionException>? onMachineProtectorFailed = null,
                                    Func<ConnectionFileKey, bool>? keyValidator = null)
    {
        if (HasMachineProtector)
        {
            KeyProtectionException? machineFailure = null;

            // The slot that opened this file last time, first. Nothing else knows which slot belongs
            // to this account — there is no index and no label — so without the session's memory
            // every read walks the list again, and on a file shared by a team the member listed last
            // pays for every member ahead of them on every read.
            foreach (string slot in InSessionOrder(_machineSlots))
            {
                ConnectionFileKey? candidate = null;
                try
                {
                    candidate = DpapiKeyProtector.Unwrap(slot);
                    if (Accepts(candidate, keyValidator))
                    {
                        ConnectionFileKey opened = candidate;
                        candidate = null;
                        MachineSlotSession.Remember(slot);
                        HasSlotForThisAccount = true;
                        return opened;
                    }

                    // Unwrapped, and not this file's key. `ProtectedData.Unprotect` succeeding proves
                    // the blob was written by this account, not that it belongs to this file: a slot
                    // copied from another file the same user owns unwraps perfectly and yields the
                    // wrong key. So the search continues rather than stopping at the first slot this
                    // account can open — accepting it would open the store onto contents that do not
                    // decrypt, with no prompt and nothing reported.
                    machineFailure ??= new KeyProtectionException(
                        KeyProtector.Machine, KeyProtectionFailure.Unusable,
                        "A machine protector on this connection file belongs to a different key, so it " +
                        "was left over from an earlier one or copied from another file.");
                }
                catch (KeyProtectionException ex)
                {
                    // Another member's slot, in the ordinary case: it was written by an account that
                    // is not this one, and DPAPI refuses it quickly and without asking for anything.
                    machineFailure ??= ex;
                }
                finally
                {
                    candidate?.Dispose();
                }
            }

            // Reported once, after the whole list, rather than once per slot. On a shared file most
            // of the slots are expected to fail — they belong to other people — and a warning per
            // member would turn a normal open into a wall of alarming text.
            if (machineFailure is not null)
                onMachineProtectorFailed?.Invoke(machineFailure);
        }

        if (recoveryPasswordRequestor is null)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword, KeyProtectionFailure.NotSupplied,
                "This connection file needs its recovery password, and nothing was available to ask for it.");

        KeyProtectionException? lastFailure = null;
        for (int attempt = 0; attempt < MaxRecoveryPasswordAttempts; attempt++)
        {
            Optional<SecureString> provided = recoveryPasswordRequestor();
            if (!provided.Any())
                break;

            SecureString password = provided.First();
            if (password is null || password.Length == 0)
                break;

            ConnectionFileKey? candidate = null;
            try
            {
                candidate = RecoveryPasswordKeyProtector.Unwrap(RecoveryProtector, password);
                if (Accepts(candidate, keyValidator))
                {
                    ConnectionFileKey opened = candidate;
                    candidate = null;
                    return opened;
                }

                // The password was right and the key is not this file's, so the protector belongs to
                // another file. Re-asking would spend the remaining attempts on a password that has
                // already been shown to be correct.
                throw new KeyProtectionException(KeyProtector.RecoveryPassword, KeyProtectionFailure.Unusable,
                    "The recovery password opened this file's protector, but the key it holds does " +
                    "not belong to this file.");
            }
            catch (KeyProtectionException ex) when (ex.IsRetryable)
            {
                lastFailure = ex;
            }
            finally
            {
                candidate?.Dispose();
            }

            // A non-retryable failure escapes the loop uncaught, deliberately. Those describe the
            // protector — truncated, a format this build does not know, parameters outside the range
            // it writes — not the password, so spending the remaining attempts on it would ask twice
            // more for something no password opens and leave the user believing they typed it wrong.
        }

        throw lastFailure ?? new KeyProtectionException(KeyProtector.RecoveryPassword,
            KeyProtectionFailure.NotSupplied,
            "This connection file was not opened: no recovery password was given.");
    }

    /// <summary>
    /// Whether an unwrapped key is accepted. No validator means any key that unwrapped is taken,
    /// which is only correct for a caller that has nothing to check it against.
    /// </summary>
    private static bool Accepts(ConnectionFileKey key, Func<ConnectionFileKey, bool>? keyValidator) =>
        keyValidator is null || keyValidator(key);

    /// <summary>
    /// The slots, with the one this session already opened a file with moved to the front.
    /// </summary>
    private static IEnumerable<string> InSessionOrder(string[] slots)
    {
        string? remembered = MachineSlotSession.Peek();
        if (remembered is null || slots.Length < 2)
            return slots;

        int index = Array.IndexOf(slots, remembered);
        return index <= 0 ? slots : slots.Skip(index).Concat(slots.Take(index));
    }

    /// <summary>
    /// Sets or replaces the recovery password, leaving the machine protector and the file's contents
    /// untouched.
    /// </summary>
    /// <remarks>
    /// This is what makes a forgotten recovery password survivable: on the machine that wrote the
    /// file the machine protector still works, so the key can be unwrapped and re-wrapped under a new
    /// password. Only the recovery protector changes — the file key is the same key, so nothing the
    /// file holds is re-encrypted and a rolling backup taken before the change still opens.
    /// </remarks>
    public ConnectionFileKeyProtection WithRecoveryPassword(ConnectionFileKey fileKey,
                                                            SecureString recoveryPassword,
                                                            int iterations = RecoveryPasswordKeyProtector.DefaultIterations,
                                                            HashAlgorithmName? prf = null)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        // The generation is carried over, because this is the same key: nothing another session holds
        // has been invalidated, and refusing their next save would be a lie about what happened here.
        return new ConnectionFileKeyProtection(_machineSlots,
            RecoveryPasswordKeyProtector.Wrap(fileKey, recoveryPassword, iterations, prf),
            Generation, HasSlotForThisAccount);
    }

    /// <summary>
    /// Adds a slot for this account, keeping the slots already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Additive, because the slots already in the file belong to other people. Replacing them would
    /// make a shared file serve one member at a time, each save locking out whoever saved before —
    /// which is the defect this change exists to remove, not a smaller version of it.
    /// </para>
    /// <para>
    /// <b>A lost slot costs one prompt.</b> Two members saving at once loses one of the writes, as it
    /// does today for the whole file, and if the lost write carried a slot that member supplies the
    /// recovery password on their next open and is slotted again when they next save. That is why
    /// this needs no locking. Note what it depends on: the repair happens when *they* next save, so a
    /// member who only ever reads is prompted every time and never repaired.
    /// </para>
    /// </remarks>
    /// <exception cref="KeyProtectionException">The file already carries the maximum number of slots.</exception>
    public ConnectionFileKeyProtection WithMachineProtector(ConnectionFileKey fileKey)
    {
        ArgumentNullException.ThrowIfNull(fileKey);

        if (_machineSlots.Length >= MaxMachineSlots)
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                $"This connection file already carries {MaxMachineSlots} machine protectors, which is " +
                "the most it may have. Rekeying the file drops them and starts again.");

        // Same key, so same generation. A slot earned on save must not make anyone else's next save
        // look like a save from before a rekey — that is an ordinary save, and ordinary saves are
        // last-writer-wins by design.
        return new ConnectionFileKeyProtection([.. _machineSlots, DpapiKeyProtector.Wrap(fileKey)],
            RecoveryProtector, Generation, hasSlotForThisAccount: true);
    }

    /// <summary>
    /// Reads the protectors off a root element's attribute values, or null when the file carries none.
    /// </summary>
    /// <remarks>
    /// A recovery protector on its own is a complete file — that is the portable edition. A machine
    /// protector on its own is not, and is refused rather than accepted as a file that happens to open
    /// today: it would be a store nobody could ever recover, which is the failure this whole change
    /// exists to prevent.
    /// </remarks>
    /// <exception cref="KeyProtectionException">
    /// The file declares a machine protector and no recovery protector.
    /// </exception>
    /// <param name="generation">
    /// The file's key generation, or null in a file written before generations existed. Present and
    /// blank is refused for the same reason an empty slot list is: nothing this application writes
    /// produces it, and reading it as "no generation" would silently disarm the check that stops a
    /// stale session undoing a rekey.
    /// </param>
    public static ConnectionFileKeyProtection? Read(string? machineProtector, string? recoveryProtector,
                                                    string? generation = null)
    {
        bool hasMachine = machineProtector is not null;
        bool hasRecovery = !string.IsNullOrWhiteSpace(recoveryProtector);

        if (!hasMachine && !hasRecovery)
            return null;

        if (!hasRecovery)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword, KeyProtectionFailure.Unusable,
                "This connection file carries a machine protector but no recovery protector, so it " +
                "could never be opened anywhere else. It was not written by this application.");

        if (generation is not null && string.IsNullOrWhiteSpace(generation))
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                "This connection file declares a key generation that holds nothing. It was not " +
                "written by this application.");

        return new ConnectionFileKeyProtection(ReadSlots(machineProtector), recoveryProtector!, generation);
    }

    /// <summary>
    /// Splits the machine protector attribute into slots, refusing the shapes that mean something
    /// went wrong.
    /// </summary>
    /// <remarks>
    /// An absent attribute is a file with no machine protector, which is a portable file or one
    /// written outside the user profile before slots existed. An attribute that is <i>present</i> and
    /// holds nothing is not the same thing and is refused: nothing this application writes produces
    /// it, so reading it as "no machine protector" would silently accept a file that something else
    /// has damaged, and then prompt for the recovery password as though that were normal.
    /// </remarks>
    private static string[] ReadSlots(string? machineProtector)
    {
        if (machineProtector is null)
            return [];

        string[] slots = machineProtector.Split(SlotSeparator, StringSplitOptions.TrimEntries);

        if (slots.Length == 0 || slots.Any(string.IsNullOrWhiteSpace))
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                "This connection file declares a machine protector that holds nothing, or that has an " +
                "empty entry in its list. It was not written by this application.");

        // Before any of them is tried, which is the point: the cost of a slot is a DPAPI call, so a
        // file declaring a hundred thousand would otherwise take minutes to be refused.
        if (slots.Length > MaxMachineSlots)
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                $"This connection file declares {slots.Length} machine protectors, and no file written " +
                $"by this application has more than {MaxMachineSlots}.");

        return slots;
    }

    public void WriteTo(XElement rootElement)
    {
        ArgumentNullException.ThrowIfNull(rootElement);

        // Unconditional, because passing null is what *removes* an attribute. Writing only when
        // present would leave a stale protector on an element that already carried one — and a stale
        // machine protector is the worst possible leftover: it still unwraps, but to the previous
        // file key, and Unwrap prefers it over the recovery protector. The file would open onto
        // contents that no longer decrypt, with no prompt and nothing reported.
        // One slot writes exactly what it wrote before the list existed, so a file with a single
        // member is byte-identical to what the preceding change produced: no old form, no new form,
        // no version flag on the attribute, nothing to migrate.
        rootElement.SetAttributeValue(XName.Get(MachineProtectorAttributeName),
                                      HasMachineProtector ? string.Join(SlotSeparator, _machineSlots) : null);

        rootElement.SetAttributeValue(XName.Get(RecoveryProtectorAttributeName), RecoveryProtector);

        // Null removes it, which is what keeps a file written before generations existed free of one
        // until it is rekeyed. See <see cref="Generation"/> for why it is not filled in here.
        rootElement.SetAttributeValue(XName.Get(GenerationAttributeName), Generation);
    }
}
