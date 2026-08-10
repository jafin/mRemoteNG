using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI.TaskDialog;

namespace mRemoteNG.UI;

/// <summary>
/// Shows the storage format confirmation. Everything it says, and everything it does with the
/// answer, lives in <see cref="StorageFormatUpgrade"/> — this only puts it on screen.
/// </summary>
/// <remarks>
/// A task dialog rather than a message box, for two reasons. The choices are three named actions and
/// "Yes / No / Cancel" would make the user guess which one keeps their file openable. And the
/// cryptographic detail belongs behind an expander: present for anyone who wants it, not competing
/// with the sentence about which applications stop working.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class StorageFormatUpgradePrompt
{
    /// <summary>
    /// Asks, acts on the answer, and reports whether the store was hardened.
    /// </summary>
    /// <remarks>
    /// Taking the classic copy returns to the question rather than ending it. The export is offered
    /// as something to do <em>before</em> deciding, so treating it as an answer would leave a user
    /// who wanted both with only the copy — and no way to tell that the store was never hardened.
    /// </remarks>
    public static bool Confirm(Control owner,
        StorageFormatStoreKind storeKind,
        RootNodeInfo rootNode,
        ConnectionTreeModel connectionTreeModel)
    {
        while (true)
        {
            StorageFormatUpgradeChoice choice = Ask(owner, storeKind);

            if (choice != StorageFormatUpgradeChoice.ExportClassicCopy)
                return StorageFormatUpgrade.Apply(rootNode, choice);

            App.Export.ExportToFile(null, connectionTreeModel);
        }
    }

    public static StorageFormatUpgradeChoice Ask(Control owner, StorageFormatStoreKind storeKind)
    {
        StorageFormatUpgradeMessage message = StorageFormatUpgrade.BuildMessage(storeKind);

        string commandButtons = StorageFormatUpgrade.CommandButtons();

        CTaskDialog.ShowTaskDialogBox(
            owner,
            Language.StorageFormatUpgradeTitle,
            message.Instruction,
            message.Content,
            message.ExpandedInfo,
            footer: "",
            verificationText: "",
            radioButtons: "",
            commandButtons: commandButtons,
            ETaskDialogButtons.None,
            ESysIcons.Warning,
            ESysIcons.Warning);

        return CTaskDialog.CommandButtonResult switch
        {
            0 => StorageFormatUpgradeChoice.Harden,
            1 => StorageFormatUpgradeChoice.ExportClassicCopy,

            // Includes -1, which is what closing the dialog without choosing reports. An unanswered
            // question about an irreversible format change is a no.
            _ => StorageFormatUpgradeChoice.Decline
        };
    }
}
