using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.UI;

/// <summary>
/// Asks whether to rekey the connection file, and does it.
/// </summary>
/// <remarks>
/// <para>
/// Rekeying is the operation that removes a member's access, and the confirmation is most of its
/// value. A user who deletes a slot and believes they have revoked access is worse off than one who
/// was told what revocation actually costs — so what a rekey cannot do is stated as plainly as what
/// it does, before anything is changed.
/// </para>
/// <para>
/// The three paragraphs are ordered by what the user needs in order to answer: what it changes, what
/// it cannot reach, and what is kept. The middle one is the one they will not think of themselves.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConnectionFileRekeyPrompt
{
    /// <summary>
    /// Collects the new recovery password. Verified by re-entry, as every other path that sets one
    /// is: it is typed once and needed on a day when the machine it was set on may be gone.
    /// </summary>
    internal static Func<string, Optional<SecureString>> PasswordPrompt { get; set; } =
        name => MiscTools.PasswordDialog(name, verify: true);

    /// <summary>Shown so tests can assert what was said rather than only what was done.</summary>
    internal static Action<Control?, string, string> ShowMessage { get; set; } =
        (owner, text, title) => MessageBox.Show(owner, text, title, MessageBoxButtons.OK,
                                                MessageBoxIcon.Information);

    /// <summary>Asked before anything is changed. True rekeys.</summary>
    internal static Func<Control?, string, bool> Confirm { get; set; } =
        (owner, text) => MessageBox.Show(owner, text, Language.RekeyTitle, MessageBoxButtons.YesNo,
                                         MessageBoxIcon.Warning) == DialogResult.Yes;

    public static void Ask(Control? owner)
    {
        RootNodeInfo? root = Runtime.ConnectionsService.ConnectionTreeModel?.RootNodes
            .OfType<RootNodeInfo>().FirstOrDefault();

        if (root is null)
            return;

        if (root.KeyProtection is null)
        {
            ShowMessage(owner, Language.RekeyNotProtected, Language.RekeyTitle);
            return;
        }

        if (!Confirm(owner, BuildExplanation()))
        {
            ShowMessage(owner, Language.RekeyDeclined, Language.RekeyTitle);
            return;
        }

        Optional<SecureString> supplied = PasswordPrompt(Language.RekeyNewPasswordName);
        SecureString? newPassword = supplied.Any() ? supplied.First() : null;

        try
        {
            if (newPassword is not { Length: > 0 })
            {
                // Declining the password is declining the rekey. Applying it without one would leave
                // a store nobody could open anywhere else, and this is the last moment at which that
                // is still recoverable.
                ShowMessage(owner, Language.RekeyDeclined, Language.RekeyTitle);
                return;
            }

            // The menu item is enabled only with a file loaded, so this is an impossible state rather
            // than a user error — reported through the catch below rather than silently doing nothing.
            string fileName = Runtime.ConnectionsService.ConnectionFileName
                ?? throw new InvalidOperationException(
                    "No connection file is loaded, so there is nothing to rekey.");

            // Before the rekey, and of the file as it stands. A rekey that half-completes on a share
            // is the one path here that can lose a store, and the backup is what makes that
            // survivable — it still opens with the old recovery password, which the user is told.
            FileBackupCreator.CreateBackupFile(fileName);

            ConnectionFileRekey.Apply(root, newPassword,
                includeMachineProtector: !Runtime.IsPortableEdition);

            // The save is what re-encrypts the contents: the saver encrypts with whatever key the
            // root carries, and the root now carries the new one.
            Runtime.ConnectionsService.SaveConnections();

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"Connection file '{fileName}' was rekeyed. Every previous machine protector and the " +
                "old recovery password no longer open it.", true);

            ShowMessage(owner, Language.RekeyDone, Language.RekeyTitle);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage("Rekeying the connection file failed", ex);
        }
        finally
        {
            newPassword?.Dispose();
        }
    }

    /// <summary>
    /// The confirmation text. Assembled here rather than at the dialog so a test can assert what a
    /// security warning says without needing a window.
    /// </summary>
    internal static string BuildExplanation() =>
        string.Join(Environment.NewLine + Environment.NewLine,
            Language.RekeyInstruction, Language.RekeyWhat, Language.RekeyLimit, Language.RekeyBackup);
}
