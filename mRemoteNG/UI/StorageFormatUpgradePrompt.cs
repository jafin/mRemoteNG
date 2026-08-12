using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
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

            if (choice == StorageFormatUpgradeChoice.ExportClassicCopy)
            {
                App.Export.ExportToFile(null, connectionTreeModel);
                continue;
            }

            // The recovery password is part of hardening a connection file, not a step after it. A
            // store raised to the hardened level without one would be encrypted under a key bound to
            // this Windows account and nothing else — and its entire backup history would go with the
            // profile, silently, with no signal until the day a backup was needed.
            if (choice == StorageFormatUpgradeChoice.Harden &&
                storeKind == StorageFormatStoreKind.ConnectionFile &&
                !EstablishProtection(owner, rootNode))
            {
                return false;
            }

            return StorageFormatUpgrade.Apply(rootNode, choice);
        }
    }

    /// <summary>
    /// Explains why a recovery password is needed, collects it, and gives the store its own key.
    /// </summary>
    /// <remarks>
    /// Declining is an answer and leaves the store exactly as it was: still classic, still under
    /// whatever key it already had. That is why this runs <em>before</em>
    /// <see cref="StorageFormatUpgrade.Apply"/> rather than after — a level raised and then abandoned
    /// would leave a store that claims to be hardened and is not.
    /// </remarks>
    private static bool EstablishProtection(Control owner, RootNodeInfo rootNode)
    {
        if (ConnectionFileMigration.IsAlreadyProtected(rootNode))
            return true;

        bool machineProtector = MachineProtectorPolicy.ShouldWriteMachineProtector(
            Runtime.ConnectionsService.ConnectionFileName, Runtime.IsPortableEdition);

        MessageBox.Show(owner,
            StorageFormatUpgrade.BuildRecoveryPasswordExplanation(machineProtector, Runtime.IsPortableEdition),
            Language.RecoveryPasswordTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

        Optional<SecureString> supplied = PasswordPrompt(Language.RecoveryPasswordName);
        if (!supplied.Any() || supplied.First() is not { Length: > 0 } recoveryPassword)
        {
            MessageBox.Show(owner, Language.RecoveryPasswordDeclined, Language.RecoveryPasswordTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        ConnectionFileMigration.Establish(rootNode, recoveryPassword, machineProtector,
            Properties.OptionsSecurityPage.Default.EncryptionKeyDerivationIterations);

        // Remembered so the store does not ask for it again this run — it was just typed twice.
        RecoveryPasswordSession.Remember(recoveryPassword);
        return true;
    }

    /// <summary>
    /// Collects the recovery password. Replaceable so a test can drive the decision without a modal
    /// dialog, exactly as <see cref="MasterPasswordGate.PasswordPrompt"/> is.
    /// </summary>
    /// <remarks>
    /// Verified by re-entry, because it is typed once and needed years later, on a day when the
    /// machine it was set on is gone. A typo here is not recoverable by anything.
    /// </remarks>
    internal static Func<string, Optional<SecureString>> PasswordPrompt { get; set; } =
        name => MiscTools.PasswordDialog(name, verify: true);

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
