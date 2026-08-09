using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;
using mRemoteNG.UI.TaskDialog;

namespace mRemoteNG.UI.Controls.FileTransfer
{
    /// <summary>
    /// Asks the user, once per transfer, what to do about files that already exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the application's own task dialog rather than a message box: four named choices do not fit
    /// the three anonymous buttons a <see cref="MessageBox"/> offers, and "Yes / No / Cancel" would make
    /// the user guess which of them means "overwrite if newer".
    /// </para>
    /// <para>
    /// Marshals to the UI thread itself. The caller is a directory walk running in the background, and
    /// making every caller remember to marshal is how one of them eventually does not.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class TransferConflictPrompt(Control owner) : ITransferConflictResolver
    {
        private readonly Control _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public Task<TransferConflictResolution> ResolveAsync(FileSystemEntry source,
                                                             FileSystemEntry destination,
                                                             CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);

            cancellationToken.ThrowIfCancellationRequested();

            if (_owner.IsDisposed || !_owner.IsHandleCreated)
                return Task.FromResult(TransferConflictResolution.Cancel);

            if (!_owner.InvokeRequired)
                return Task.FromResult(Ask(source, destination));

            TaskCompletionSource<TransferConflictResolution> completion = new();

            _owner.BeginInvoke(() =>
            {
                try
                {
                    completion.TrySetResult(Ask(source, destination));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            return completion.Task;
        }

        private TransferConflictResolution Ask(FileSystemEntry source, FileSystemEntry destination)
        {
            string content = string.Format(CultureInfo.CurrentCulture,
                                           Language.TransferConflictContent,
                                           FilePaneControl.DescribeSize(source.Length),
                                           source.LastWriteTime,
                                           FilePaneControl.DescribeSize(destination.Length),
                                           destination.LastWriteTime);

            string commandButtons = string.Join(" | ",
                                                Language.TransferOverwriteAll,
                                                Language.TransferSkipAll,
                                                Language.TransferOverwriteIfNewer,
                                                Language._Cancel);

            CTaskDialog.ShowTaskDialogBox(
                _owner,
                Language.TransferConflictTitle,
                string.Format(CultureInfo.CurrentCulture, Language.TransferConflictInstruction, destination.FullPath),
                content,
                expandedInfo: "",
                footer: "",
                verificationText: "",
                radioButtons: "",
                commandButtons: commandButtons,
                ETaskDialogButtons.None,
                ESysIcons.Question,
                ESysIcons.Question);

            return CTaskDialog.CommandButtonResult switch
            {
                0 => TransferConflictResolution.OverwriteAll,
                1 => TransferConflictResolution.SkipAll,
                2 => TransferConflictResolution.OverwriteIfNewer,

                // Includes -1, which is what closing the dialog without choosing reports. Treating an
                // unanswered overwrite question as "go ahead" is how data gets lost.
                _ => TransferConflictResolution.Cancel
            };
        }
    }
}
