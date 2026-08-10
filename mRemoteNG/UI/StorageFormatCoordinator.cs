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
    /// Offers the upgrade if this store has never been offered it. Silent otherwise.
    /// </summary>
    /// <remarks>
    /// A security feature nobody discovers shipped for nothing, and a prompt that returns every
    /// session teaches users to dismiss everything the application says — including the messages
    /// that matter. Once per store settles both.
    /// </remarks>
    public static void OfferIfDue(Control owner)
    {
        if (!TryGetStore(out RootNodeInfo? root, out string? storePath) || root is null || storePath is null)
            return;

        if (Runtime.ConnectionsService.UsingDatabase)
            return;

        if (!StorageFormatOffer.ShouldOffer(root.StorageFormat, StorageFormatStoreKind.ConnectionFile,
                OfferLog.WasDeclined(storePath)))
            return;

        if (Ask(owner, root))
            return;

        // Declining is an answer. It stops the offer; it does not put the upgrade out of reach —
        // the File menu keeps it available.
        if (!OfferLog.RecordDecline(storePath))
            Runtime.MessageCollector?.AddMessage(Messages.MessageClass.WarningMsg,
                $"Could not record the storage format decision beside \"{storePath}\". The offer will appear again.");
    }

    /// <summary>
    /// Opens the confirmation because the user asked for it, whatever they answered before.
    /// </summary>
    public static void AskOnRequest(Control owner)
    {
        if (!TryGetStore(out RootNodeInfo? root, out _) || root is null)
            return;

        StorageFormatStoreKind kind = Runtime.ConnectionsService.UsingDatabase
            ? StorageFormatStoreKind.SqlDatabase
            : StorageFormatStoreKind.ConnectionFile;

        if (root.StorageFormat == StorageFormatLevel.Hardened)
        {
            MessageBox.Show(owner, Resources.Language.Language.StorageFormatAlreadyHardened,
                Resources.Language.Language.StorageFormatUpgradeTitle, MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Ask(owner, root, kind);
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
