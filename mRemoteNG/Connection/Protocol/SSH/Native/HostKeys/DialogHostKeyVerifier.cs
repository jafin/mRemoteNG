using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;

/// <summary>
/// Puts the host key in front of the user and waits for an answer.
/// </summary>
/// <remarks>
/// Blocks the connecting thread until the dialog is dismissed, which is the point: the session must
/// not proceed while the question is unanswered.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DialogHostKeyVerifier(Control marshalTarget) : IHostKeyVerifier
{
    private readonly Control _marshalTarget = marshalTarget ?? throw new ArgumentNullException(nameof(marshalTarget));

    public bool Accept(HostKeyPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (_marshalTarget.IsDisposed || !_marshalTarget.IsHandleCreated)
            return false;      // nowhere to ask, so nothing to accept

        if (_marshalTarget.InvokeRequired)
            return (bool)_marshalTarget.Invoke(() => Ask(presentation));

        return Ask(presentation);
    }

    private static bool Ask(HostKeyPresentation presentation)
    {
        string endpoint = string.Create(CultureInfo.InvariantCulture, $"{presentation.Host}:{presentation.Port}");

        string message;
        MessageBoxIcon icon;
        MessageBoxDefaultButton defaultButton;

        if (presentation.Status == HostKeyStatus.Changed)
        {
            // A changed key is either a reinstalled server or someone between you and it, and
            // nothing on this side can tell which. The default button is No for that reason.
            message = string.Format(CultureInfo.CurrentCulture, Language.SshNativeHostKeyChanged,
                endpoint, presentation.KeyAlgorithm, presentation.Fingerprint, presentation.PreviousFingerprint);
            icon = MessageBoxIcon.Warning;
            defaultButton = MessageBoxDefaultButton.Button2;
        }
        else
        {
            message = string.Format(CultureInfo.CurrentCulture, Language.SshNativeHostKeyUnknown,
                endpoint, presentation.KeyAlgorithm, presentation.Fingerprint);
            icon = MessageBoxIcon.Question;
            defaultButton = MessageBoxDefaultButton.Button2;
        }

        DialogResult result = MessageBox.Show(
            message,
            Language.SshNativeHostKeyTitle,
            MessageBoxButtons.YesNo,
            icon,
            defaultButton);

        return result == DialogResult.Yes;
    }
}
