using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;
using mRemoteNG.UI.Forms;

namespace mRemoteNG.UI.Controls.FileTransfer
{
    /// <summary>
    /// The prompts a file pane needs, behind an interface.
    /// </summary>
    /// <remarks>
    /// Dialogs are what make UI commands untestable, so they are the one thing the command layer
    /// does not do itself. A test supplies answers directly; the real implementation shows a form.
    /// </remarks>
    public interface IFilePanePrompts
    {
        /// <summary>Asks for a name, returning <see langword="null"/> if the user cancelled.</summary>
        string? AskForName(string title, string prompt, string initialValue);

        /// <summary>Asks whether to delete <paramref name="count"/> entries.</summary>
        bool ConfirmDelete(int count);
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public sealed class FilePanePrompts(IWin32Window? owner) : IFilePanePrompts
    {
        public string? AskForName(string title, string prompt, string initialValue)
        {
            using FrmInputBox input = new(title, prompt, initialValue);

            return input.ShowDialog(owner) == DialogResult.OK ? input.returnValue : null;
        }

        public bool ConfirmDelete(int count)
        {
            string message = string.Format(CultureInfo.CurrentCulture, Language.ConfirmDeleteEntries, count);

            return MessageBox.Show(owner, message, Language.Delete,
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }

    /// <summary>
    /// The mutation commands a pane offers, separated from the control so they can be tested.
    /// </summary>
    /// <remarks>
    /// Each command is "ask the user, then tell the controller". Splitting the asking out is what
    /// makes the interesting cases assertable: that cancelling a prompt changes nothing, and that
    /// declining a delete confirmation really does leave the entry alone.
    /// </remarks>
    public sealed class FilePaneCommands(FilePaneController controller, IFilePanePrompts prompts)
    {
        private readonly FilePaneController _controller =
            controller ?? throw new ArgumentNullException(nameof(controller));

        private readonly IFilePanePrompts _prompts =
            prompts ?? throw new ArgumentNullException(nameof(prompts));

        public async Task<bool> NewFolderAsync()
        {
            string? name = _prompts.AskForName(Language.NewFolder, Language.EnterFolderName, string.Empty);

            return !string.IsNullOrWhiteSpace(name) && await _controller.CreateDirectoryAsync(name.Trim());
        }

        public async Task<bool> NewFileAsync()
        {
            string? name = _prompts.AskForName(Language.NewFile, Language.EnterFileName, string.Empty);

            return !string.IsNullOrWhiteSpace(name) && await _controller.CreateFileAsync(name.Trim());
        }

        public async Task<bool> RenameAsync(FileSystemEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            string? name = _prompts.AskForName(Language.Rename, Language.EnterNewName, entry.Name);

            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal))
                return false;

            return await _controller.RenameAsync(entry, name.Trim());
        }

        /// <summary>
        /// Deletes the given entries after one confirmation for the whole set.
        /// </summary>
        /// <remarks>
        /// One prompt, not one per entry: confirming twenty times is a prompt people learn to click
        /// through, which is worse than not asking.
        /// </remarks>
        public async Task<int> DeleteAsync(IReadOnlyList<FileSystemEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0 || !_prompts.ConfirmDelete(entries.Count))
                return 0;

            int deleted = 0;

            foreach (FileSystemEntry entry in entries)
            {
                // Each failure is already reported by the controller. Carrying on means one
                // protected file does not abandon the rest of the selection.
                if (await _controller.DeleteAsync(entry))
                    deleted++;
            }

            return deleted;
        }
    }
}
