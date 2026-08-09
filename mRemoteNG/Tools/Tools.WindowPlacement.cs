using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.App;

namespace mRemoteNG.Tools;

[SupportedOSPlatform("windows")]
public class WindowPlacement(Form form)
{
    private Form _form = form;

    #region Public Properties

    public Form Form
    {
        get => _form;
        set => _form = value;
    }

    public bool RestoreToMaximized
    {
        get
        {
            NativeMethods.WINDOWPLACEMENT windowPlacement = GetWindowPlacement();
            return Convert.ToBoolean(windowPlacement.flags & NativeMethods.WPF_RESTORETOMAXIMIZED);
        }
        set
        {
            NativeMethods.WINDOWPLACEMENT windowPlacement = GetWindowPlacement();
            if (value)
            {
                windowPlacement.flags |= NativeMethods.WPF_RESTORETOMAXIMIZED;
            }
            else
            {
                windowPlacement.flags &= ~NativeMethods.WPF_RESTORETOMAXIMIZED;
            }

            SetWindowPlacement(windowPlacement);
        }
    }

    #endregion

    #region Private Functions

    private NativeMethods.WINDOWPLACEMENT GetWindowPlacement()
    {
        if (_form == null)
        {
            throw new InvalidOperationException("WindowPlacement.Form is not set.");
        }

        NativeMethods.WINDOWPLACEMENT windowPlacement = new();
        windowPlacement.length = (uint)Marshal.SizeOf(windowPlacement);
        NativeMethods.GetWindowPlacement(_form.Handle, ref windowPlacement);
        return windowPlacement;
    }

    private void SetWindowPlacement(NativeMethods.WINDOWPLACEMENT windowPlacement)
    {
        if (_form == null)
        {
            throw new InvalidOperationException("WindowPlacement.Form is not set.");
        }

        windowPlacement.length = (uint)Marshal.SizeOf(windowPlacement);
        NativeMethods.SetWindowPlacement(_form.Handle, ref windowPlacement);
    }

    #endregion
}