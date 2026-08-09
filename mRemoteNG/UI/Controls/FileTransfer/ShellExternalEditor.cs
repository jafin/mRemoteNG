using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.UI.Controls.FileTransfer;

/// <summary>
/// Opens a file with whatever the shell associates with it.
/// </summary>
/// <remarks>
/// <c>UseShellExecute</c> so the user's own choice of editor is honoured. mRemoteNG does not
/// pick an editor and does not add a setting for one: the association already exists and is
/// where a user expects to change it.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShellExternalEditor : IExternalEditor
{
    public void Open(string localPath)
    {
        ArgumentNullException.ThrowIfNull(localPath);

        using Process? process = Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
    }
}

/// <inheritdoc />
[SupportedOSPlatform("windows")]
public sealed class EditPrompts(IWin32Window? owner) : IEditPrompts
{
    public bool ConfirmUpload(string fileName)
    {
        string message = string.Format(CultureInfo.CurrentCulture, Language.ConfirmUploadEditedFile, fileName);

        return MessageBox.Show(owner, message, Language.FileManager,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}