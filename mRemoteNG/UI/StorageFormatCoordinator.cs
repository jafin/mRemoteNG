using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Security;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.UI;

/// <summary>
/// The two ways a user meets the storage format decision: offered once when a classic connection
/// file is opened, and available on request forever after.
/// </summary>
/// <remarks>
/// Both routes end in the same confirmation. What differs is only whether the application raised the
/// subject or the user did — and whether a refusal is recorded.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class StorageFormatCoordinator
{
    private static readonly IStorageFormatOfferLog OfferLog = new SidecarStorageFormatOfferLog();

    /// <summary>
    /// Whether a confirmation is on screen. UI thread only, so a plain field is the right amount of
    /// machinery — a lock here would suggest a contention that cannot happen.
    /// </summary>
    private static bool _confirmationIsOpen;

    /// <summary>
    /// Offers the upgrade if this store has never been offered it. Silent otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A security feature nobody discovers shipped for nothing, and a prompt that returns every
    /// session teaches users to dismiss everything the application says — including the messages
    /// that matter. Once per store settles both.
    /// </para>
    /// <para>
    /// <b>Guarded against re-entry.</b> This is posted from every <c>ConnectionsLoaded</c>, and the
    /// store reloads for reasons that have nothing to do with the user: an external edit to the
    /// file, a recovery from backup, a switch between files. A posted callback still runs while a
    /// modal dialog is pumping messages, so without the guard each reload stacks another
    /// confirmation on top of the last and the user is asked a question they cannot answer once.
    /// </para>
    /// </remarks>
    public static void OfferIfDue(Control owner)
    {
        if (_confirmationIsOpen)
            return;

        if (!TryGetStore(out RootNodeInfo? root, out string? storePath) || root is null || storePath is null)
            return;

        if (Runtime.ConnectionsService.UsingDatabase)
            return;

        if (!StorageFormatOffer.ShouldOffer(root.StorageFormat, StorageFormatStoreKind.ConnectionFile,
                OfferLog.WasDeclined(storePath), root.KeyProtection is not null))
            return;

        _confirmationIsOpen = true;
        try
        {
            if (Ask(owner, root))
                return;

            // Declining is an answer. It stops the offer; it does not put the upgrade out of reach —
            // the File menu keeps it available.
            if (!OfferLog.RecordDecline(storePath))
                Runtime.MessageCollector?.AddMessage(Messages.MessageClass.WarningMsg,
                    $"Could not record the storage format decision beside \"{storePath}\". The offer will appear again.");
        }
        finally
        {
            _confirmationIsOpen = false;
        }
    }

    /// <summary>
    /// Opens the confirmation because the user asked for it, whatever they answered before.
    /// </summary>
    /// <remarks>
    /// Shares the re-entry guard with <see cref="OfferIfDue"/> rather than keeping its own. The menu
    /// is unreachable while a modal dialog is up, so this cannot re-enter itself — but an automatic
    /// offer posted by a reload can land on top of a confirmation the user opened deliberately, and
    /// one flag covering both is what stops it.
    /// </remarks>
    public static void AskOnRequest(Control owner)
    {
        if (_confirmationIsOpen)
            return;

        if (!TryGetStore(out RootNodeInfo? root, out _) || root is null)
            return;

        StorageFormatStoreKind kind = Runtime.ConnectionsService.UsingDatabase
            ? StorageFormatStoreKind.SqlDatabase
            : StorageFormatStoreKind.ConnectionFile;

        // Not `StorageFormat == Hardened`. A connection file raised to the hardened level before
        // per-file keys existed has a stretched KDF over the published default constant and no key
        // of its own, so sending it away here left the users who took the earlier security upgrade
        // as the only ones who could not take this one.
        if (StorageFormatOffer.IsFullyHardened(root.StorageFormat, kind, root.KeyProtection is not null))
        {
            MessageBox.Show(owner, Resources.Language.Language.StorageFormatAlreadyHardened,
                Resources.Language.Language.StorageFormatUpgradeTitle, MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _confirmationIsOpen = true;
        try
        {
            Ask(owner, root, kind);
        }
        finally
        {
            _confirmationIsOpen = false;
        }
    }

    private static bool Ask(Control owner, RootNodeInfo root,
        StorageFormatStoreKind kind = StorageFormatStoreKind.ConnectionFile)
    {
        var model = Runtime.ConnectionsService.ConnectionTreeModel;
        if (model is null)
            return false;

        if (!StorageFormatUpgradePrompt.Confirm(owner, kind, root, model))
            return false;

        // The level is a property of the store, so it is not raised until the store records it.
        Runtime.ConnectionsService.SaveConnections();
        return true;
    }

    private static bool TryGetStore(out RootNodeInfo? root, out string? storePath)
    {
        root = null;
        storePath = Runtime.ConnectionsService.ConnectionFileName;

        if (!Runtime.ConnectionsService.IsConnectionsFileLoaded)
            return false;

        root = Runtime.ConnectionsService.ConnectionTreeModel?.RootNodes
            .OfType<RootNodeInfo>()
            .FirstOrDefault();

        return root is not null;
    }
}
