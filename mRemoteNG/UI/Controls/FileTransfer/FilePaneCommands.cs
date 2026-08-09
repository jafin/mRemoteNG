using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;
using mRemoteNG.UI.Forms;

namespace mRemoteNG.UI.Controls.FileTransfer;

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

    /// <summary>
    /// Asks whether to delete a selection that includes directories, contents and all.
    /// </summary>
    /// <remarks>
    /// A question of its own rather than <see cref="ConfirmDelete"/>, whose message counts the
    /// selected items and would read as though only those were going. This one has to say that
    /// everything underneath goes too, because it does, and because the count cannot be known
    /// without first walking a tree that may take minutes to read.
    /// </remarks>
    bool ConfirmRecursiveDelete(string what);
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

    public bool ConfirmRecursiveDelete(string what)
    {
        string message = string.Format(CultureInfo.CurrentCulture, Language.ConfirmDeleteRecursive, what);

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
    /// Confirms a deletion and decides how it should be carried out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One prompt, not one per entry: confirming twenty times is a prompt people learn to click
    /// through, which is worse than not asking.
    /// </para>
    /// <para>
    /// A selection containing a directory is not deleted here. It is handed back to the caller to
    /// queue, so the user can watch it and stop it — which is the thing that makes deleting a tree
    /// acceptable rather than reckless. Files alone keep the immediate path they have always had:
    /// there is nothing to watch, and a queue would be ceremony.
    /// </para>
    /// </remarks>
    /// <returns>
    /// How many entries were deleted immediately. Zero when the user declined, and zero when the
    /// work was raised through <see cref="RecursiveDeleteRequested"/> instead.
    /// </returns>
    public async Task<int> DeleteAsync(IReadOnlyList<FileSystemEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return 0;

        bool containsDirectory = entries.Any(entry => entry.IsDirectory && !entry.IsSymbolicLink);

        if (containsDirectory)
        {
            if (!_prompts.ConfirmRecursiveDelete(Describe(entries)))
                return 0;

            RecursiveDeleteRequested?.Invoke(this, entries);
            return 0;
        }

        if (!_prompts.ConfirmDelete(entries.Count))
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

    /// <summary>
    /// Raised when a confirmed deletion covers directories and must be queued rather than run here.
    /// </summary>
    public event EventHandler<IReadOnlyList<FileSystemEntry>>? RecursiveDeleteRequested;

    /// <summary>Names the selection for the confirmation: one entry by name, several by count.</summary>
    private static string Describe(IReadOnlyList<FileSystemEntry> entries) =>
        entries.Count == 1
            ? entries[0].Name
            : string.Format(CultureInfo.CurrentCulture, "{0} selected items", entries.Count);
}