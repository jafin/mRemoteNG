using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Xml;
using mRemoteNG.App;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class XmlConnectionsSaver : ISaver<ConnectionTreeModel>
{
    private readonly string _connectionFileName;
    private readonly SaveFilter _saveFilter;

    public XmlConnectionsSaver(string connectionFileName, SaveFilter saveFilter)
    {
        if (string.IsNullOrEmpty(connectionFileName))
            throw new ArgumentException($"Argument '{nameof(connectionFileName)}' cannot be null or empty", nameof(connectionFileName));
        _connectionFileName = connectionFileName;
        _saveFilter = saveFilter ?? throw new ArgumentNullException(nameof(saveFilter));
    }

    /// <summary>
    /// Writes the store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why concurrent saves cannot produce a file whose slots and contents disagree.</b> That is
    /// the one race here that would be destructive rather than annoying — a file that opens, on a key
    /// that decrypts nothing in it. It cannot happen: the protectors and the contents are produced by
    /// one serialization of one root node, so they always describe the same key, and
    /// <see cref="FileDataProvider.Save"/> writes to a temporary file and replaces the original in one
    /// step, so a reader sees one writer's whole file or the other's, never halves of both. Two
    /// members saving at once therefore loses a write, which is what saving a shared file has always
    /// done; it does not corrupt one.
    /// </para>
    /// <para>
    /// <b>The rekey check is not a lock, and the residual window is stated rather than implied.</b>
    /// The generation is read from disk when the save begins and again immediately before the
    /// replace, so a rekey landing during the expensive part — deriving the key, serializing and
    /// encrypting the tree — is caught. One landing after the second read is not: closing that would
    /// need the file held open exclusively from the first read to the last write, which would make
    /// one member's save block everyone else's rather than lose to it. What remains is a window of
    /// the replace itself, in which a save begun before a rekey can still write the superseded key
    /// back. A rekey is a deliberate act taken when someone is being removed, so the file being
    /// re-saved in that same instant by a session that has not seen it is worth naming and not worth
    /// serialising every save for.
    /// </para>
    /// </remarks>
    public void Save(ConnectionTreeModel connectionTreeModel, string propertyNameTrigger = "")
    {
        try
        {
            // One read of the root element, before anything else: both of the things that can refuse
            // this save are recorded in the file that is about to be overwritten.
            (string? recordedLevel, string? recordedGeneration) = ReadExistingRoot();
            ThrowIfLevelIsUnrecognised(recordedLevel);

            RootNodeInfo? rootNode = connectionTreeModel.RootNodes.OfType<RootNodeInfo>().FirstOrDefault();
            if (rootNode == null)
                throw new InvalidOperationException("Connection tree has no root node");

            ThrowIfRekeyedSinceThisSessionRead(rootNode, recordedGeneration);

            ICryptographyProvider cryptographyProvider = BuildProvider(rootNode);

            AdoptMachineProtectorIfDue(rootNode);

            Serializers.ISerializer<Connection.ConnectionInfo, string> xmlConnectionsSerializer = XmlConnectionSerializerFactory.Build(cryptographyProvider, connectionTreeModel, _saveFilter, Properties.OptionsSecurityPage.Default.EncryptCompleteConnectionsFile);
            string xml = xmlConnectionsSerializer.Serialize(rootNode);

            if (string.IsNullOrEmpty(xml))
                throw new InvalidOperationException("Serialized XML is empty");

            // Asked a second time, immediately before the replace. Everything between the two reads
            // — building the provider, deriving its key, serializing and encrypting the whole tree —
            // is time another member's rekey can land in, and a save that started before it would
            // otherwise write the old key and the old recovery protector back over the new ones.
            // Nothing holds the file across the two steps, so this narrows the window to the replace
            // itself rather than closing it; see the remarks on Save.
            (_, string? generationBeforeWriting) = ReadExistingRoot();
            ThrowIfRekeyedSinceThisSessionRead(rootNode, generationBeforeWriting);

            FileDataProviderWithRollingBackup fileDataProvider = new(_connectionFileName);
            fileDataProvider.Save(xml);

            // The file now holds whatever generation this store carries, so this session has seen it.
            // Without this a rekey would be saveable exactly once: the second save would compare the
            // new generation on disk against the old one read at load and refuse itself.
            rootNode.KeyGenerationSeen = rootNode.KeyProtection?.Generation;
        }
        catch (Exception ex)
        {
            // Logged here for the stack trace, then rethrown: only the caller knows whether
            // this write was the user's connection file — and so whether they need telling.
            Runtime.MessageCollector?.AddExceptionStackTrace("SaveToXml failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Gives this Windows account its own slot when the store carries none for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Task 7.5, widened by key slots. Four ordinary situations leave a store the current account
    /// cannot open silently: a file migrated outside the user profile and later moved inside, a
    /// portable file opened by the installed edition, a store migrated before a profile rebuild, and
    /// — the one slots add — <b>a file another member of a team migrated</b>, which carries their
    /// slot and not yours. Without this each of them prompts for the recovery password on every
    /// open, for ever, with nothing the user can do about it.
    /// </para>
    /// <para>
    /// <b>On save, never on open.</b> Rewriting a file that was only read turns an inspection into a
    /// permanent change, and on a shared file it would make every open a write to a file other people
    /// have open. The cost is that a member who only ever reads never earns a slot and is prompted
    /// every time — which is the right cost, since they are also the member for whom writing to the
    /// shared file is least appropriate.
    /// </para>
    /// <para>
    /// It costs nothing to fix. The file key is already unwrapped by the time anything is saved, so
    /// this wraps that same key a second way — the contents are not re-encrypted, and a rolling
    /// backup taken beforehand still opens on the recovery password.
    /// </para>
    /// <para>
    /// <b>Silent, and that is task 7.6's answer.</b> It weakens nothing: the recovery protector
    /// stays exactly as it was, so every copy of the file remains openable everywhere it was before.
    /// It is also within what the user already agreed to — they chose to protect this store with a
    /// per-file key, and this is that same key wrapped for the account already reading it. Asking
    /// would be asking whether they want the thing they asked for. It is reported to the message
    /// collector rather than being invisible.
    /// </para>
    /// <para>
    /// <b>The mirror case is deliberately not implemented.</b> A store that moves *out* of the
    /// profile keeps the protector it has. Removing one is a different act from adding one: adding
    /// only ever removes a prompt, while removing costs the owner their silent open, and on a file
    /// several people share it would be one member's save quietly changing protection for everyone.
    /// It is also unnecessary for correctness — a machine protector nobody else can use costs them a
    /// fallback, which is what they already have. If it is ever wanted it should be a decision the
    /// user sees, not a side effect of saving.
    /// </para>
    /// </remarks>
    private void AdoptMachineProtectorIfDue(RootNodeInfo rootNode)
    {
        if (rootNode.KeyProtection is null || rootNode.FileKey is null)
            return;

        // Not "does the file have a machine protector" — that question is wrong once slots exist. A
        // shared file carries other members' slots, so it has protectors and none of them is yours,
        // which is exactly the case that needs one added.
        if (rootNode.KeyProtection.HasSlotForThisAccount)
            return;

        if (!MachineProtectorPolicy.ShouldWriteMachineProtector(_connectionFileName, Runtime.IsPortableEdition))
            return;

        try
        {
            rootNode.KeyProtection = rootNode.KeyProtection.WithMachineProtector(rootNode.FileKey);
        }
        catch (KeyProtectionException ex)
        {
            // The slot list is full. This is a prompt the user keeps rather than a save that fails:
            // refusing to write their connections because a convenience could not be added would be
            // a far worse trade than the prompt it was going to remove.
            Runtime.MessageCollector?.AddExceptionMessage(
                $"Connection file '{_connectionFileName}' has no room for another machine protector, " +
                "so this account will keep being asked for the recovery password. Rekeying the file " +
                "clears the list.", ex, Messages.MessageClass.WarningMsg);
            return;
        }
        catch (CryptographicException ex)
        {
            // DPAPI itself refused. Same trade as above — the save goes ahead without the slot —
            // but deliberately a different message: rekeying clears a full list and would do
            // nothing for a Protect call that failed, so saying so here would send the user off to
            // destroy everyone else's unlock for no reason.
            Runtime.MessageCollector?.AddExceptionMessage(
                $"Windows could not protect a key for connection file '{_connectionFileName}' on " +
                "this account, so it will keep being asked for the recovery password. The file was " +
                "saved.", ex, Messages.MessageClass.WarningMsg);
            return;
        }

        Runtime.MessageCollector?.AddMessage(Messages.MessageClass.InformationMsg,
            $"Connection file '{_connectionFileName}' can now be opened on this Windows account " +
            "without its recovery password. The recovery password still works, everywhere it did before.",
            true);
    }

    /// <summary>
    /// Chooses what the store is keyed on: its own random key, or a key derived from a password.
    /// </summary>
    /// <remarks>
    /// The only place that knows both the provider and the store's protection, which is why the
    /// choice is made here rather than in the serializers underneath it.
    /// </remarks>
    private static ICryptographyProvider BuildProvider(RootNodeInfo rootNode)
    {
        if (rootNode.KeyProtection is not null)
        {
            // The second lock on the same door the reader shuts, and here for what one bypass costs.
            // XmlRootNodeSerializer writes the sentinel and the protectors only at the hardened
            // level, so keying a classic write on the file key would produce a file with no
            // protectors and contents nothing can decrypt — and FileDataProviderWithRollingBackup
            // copies before writing, so it would destroy the original and spend a backup slot on the
            // result.
            if (rootNode.StorageFormat != StorageFormatLevel.Hardened)
                throw new InvalidOperationException(
                    "This store is protected by a per-file key but is not at the hardened storage " +
                    "format, so saving it would write a file that could not be reopened.");

            // A store that declares a per-file key and cannot produce it must not be written. Falling
            // through to the settings provider would encrypt the contents under the master password
            // while the root still declared the per-file sentinel and carried protectors wrapping a
            // key nothing was encrypted with — a file that passes every structural check and decrypts
            // to nothing.
            if (rootNode.FileKey is null)
                throw new InvalidOperationException(
                    "This store is protected by a per-file key that is no longer available, so it " +
                    "cannot be saved. Re-open it and try again.");

            // Deriving nothing, so the settings' engine, mode and iteration count do not apply. The
            // key is 256 random bits; there is nothing to stretch.
            return new PerFileKeyCryptographyProvider(rootNode.FileKey);
        }

        ICryptographyProvider cryptographyProvider = new CryptoProviderFactoryFromSettings().Build();

        // A classic store keeps deriving with SHA-1 so upstream mRemoteNG, which reads this same file
        // from this same path, can still open it; the stronger function is what being hardened buys.
        cryptographyProvider.KeyDerivationPrf = rootNode.StorageFormat == StorageFormatLevel.Hardened
            ? KeyDerivationPrf.Hardened
            : KeyDerivationPrf.Default;

        return cryptographyProvider;
    }

    /// <summary>
    /// What the file about to be overwritten says about itself: its storage format level and its key
    /// generation, both null when there is nothing readable to judge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads only as far as the root element, so the cost does not scale with the file, and answers
    /// both questions from one read. A file that is absent, unreadable or not XML is not this
    /// method's business: only what was positively read can refuse a write.
    /// </para>
    /// <para>
    /// <b>Neither answer is a general conflict check.</b> Ordinary saves stay last-writer-wins,
    /// exactly as they always have been: two people editing one file is not a security boundary. What
    /// is checked is the two cases where overwriting silently discards something the file records — a
    /// format this build cannot read, and a key it has not seen.
    /// </para>
    /// </remarks>
    private (string? Level, string? Generation) ReadExistingRoot()
    {
        try
        {
            if (!File.Exists(_connectionFileName))
                return (null, null);

            using XmlReader reader = XmlReader.Create(_connectionFileName,
                new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });

            // The root element, whatever it is called: this fork writes it namespaced as
            // mrng:Connections while upstream writes a bare Connections, so matching by name would
            // silently skip one of the two shapes and check nothing.
            if (!reader.Read() || !reader.MoveToContent().Equals(XmlNodeType.Element))
                return (null, null);

            return (reader.GetAttribute(StorageFormat.AttributeName),
                    reader.GetAttribute(ConnectionFileKeyProtection.GenerationAttributeName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Not readable, so not judgeable. The save proceeds and fails on its own terms if the
            // path is genuinely broken; refusing here would block saves on any unrelated read fault.
            return (null, null);
        }
    }

    /// <summary>
    /// Refuses to write over a store whose declared format level this build cannot resolve.
    /// </summary>
    /// <remarks>
    /// The deserializer refuses to read such a file, and every save path is gated behind
    /// <c>IsConnectionsFileLoaded</c>, which a failed load clears — so this is the second lock on a
    /// door already shut. It is here because of what a save costs if the first one is ever bypassed:
    /// <see cref="FileDataProviderWithRollingBackup"/> copies the file before writing, so a save
    /// that should not have happened destroys the original <i>and</i> spends a backup slot on the
    /// result. The check is cheap and the failure is not recoverable.
    /// </remarks>
    private void ThrowIfLevelIsUnrecognised(string? recordedLevel)
    {
        if (StorageFormat.IsRecognised(recordedLevel))
            return;

        throw new NotSupportedException(
            $"Refusing to save over '{_connectionFileName}': it declares storage format " +
            $"'{recordedLevel}', which this build does not recognise. The file was written by a " +
            "newer version and overwriting it would discard that.");
    }

    /// <summary>
    /// Refuses a save from a session that has not seen the file's current key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this stops is a silent un-revocation.</b> A member rekeys the file: new key, new
    /// recovery password, every slot but their own dropped. A colleague who had the file open before
    /// that still holds the old key and the old protectors in memory, and their next ordinary save —
    /// adding a connection, moving a folder — writes the whole store back under them. The access the
    /// rekey removed is restored, by someone who never knew a rekey happened, to someone who will
    /// never be told they still have it. Nothing about that is visible afterwards: the file is
    /// well-formed and opens, on the password the rekey was meant to retire.
    /// </para>
    /// <para>
    /// <b>Compared against what this session read, not what it now holds.</b> They differ for exactly
    /// as long as a rekey is unsaved, and that is the case that must be allowed through — the session
    /// that rekeyed is the one session entitled to write a new key over the old one.
    /// </para>
    /// <para>
    /// <b>A generation on disk and none here is still a refusal.</b> That is precisely the file that
    /// predated generations and has since been rekeyed. Only the reverse — nothing on disk — passes,
    /// because a file with no generation has never been rekeyed and there is nothing to undo.
    /// </para>
    /// <para>
    /// Refusing a save is unpleasant and the message says what to do with the work in hand, because
    /// only one of the two outcomes is recoverable by the person it happens to: unsaved changes are
    /// still on screen and can be written somewhere else, while a reinstated key is invisible.
    /// </para>
    /// </remarks>
    private void ThrowIfRekeyedSinceThisSessionRead(RootNodeInfo rootNode, string? recordedGeneration)
    {
        if (recordedGeneration is null ||
            string.Equals(recordedGeneration, rootNode.KeyGenerationSeen, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"Refusing to save over '{_connectionFileName}': it has been rekeyed since this session " +
            "opened it, so saving would write these connections back under the key and recovery " +
            "password the rekey removed — restoring access for whoever it was meant to remove. " +
            "Nothing has been written and your changes are still here. Save them elsewhere with " +
            "File → Save As if you need to keep them, then re-open the connection file with its " +
            "new recovery password.");
    }
}