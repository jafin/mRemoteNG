using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using mRemoteNG.App;
using mRemoteNG.Tools.Cmdline;

namespace mRemoteNG.Tools;

[SupportedOSPlatform("windows")]
public class ProcessController : IDisposable
{
    #region Public Methods

    public bool Start(string fileName, CommandLineArguments? arguments = null)
    {
        // Validate the executable path to prevent command injection
        PathValidator.ValidateExecutablePathOrThrow(fileName, nameof(fileName));

        _process.StartInfo.UseShellExecute = false;
        _process.StartInfo.FileName = fileName;
        if (arguments != null)
            _process.StartInfo.Arguments = arguments.ToString();

        if (!_process.Start())
            return false;
        GetMainWindowHandle();

        return true;
    }

    public bool SetControlVisible(string className, string text, bool visible = true)
    {
        if (_process == null || _process.HasExited)
            return false;
        if (_handle == IntPtr.Zero)
            return false;

        IntPtr controlHandle = GetControlHandle(className, text);
        if (controlHandle == IntPtr.Zero)
            return false;

        uint nCmdShow = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
        _ = NativeMethods.ShowWindow(controlHandle, (int)nCmdShow);
        return true;
    }

    public bool SetControlText(string className, string oldText, string newText)
    {
        if (_process == null || _process.HasExited || _handle == IntPtr.Zero)
            return false;

        IntPtr controlHandle = GetControlHandle(className, oldText);
        if (controlHandle == IntPtr.Zero)
            return false;

        IntPtr result = NativeMethods.SendMessage(controlHandle, NativeMethods.WM_SETTEXT, (IntPtr)0,
            new StringBuilder(newText));
        return result.ToInt32() == NativeMethods.TRUE;
    }

    public bool SelectListBoxItem(string itemText)
    {
        if (_process == null || _process.HasExited || _handle == IntPtr.Zero)
            return false;

        IntPtr listBoxHandle = GetControlHandle("ListBox");
        if (listBoxHandle == IntPtr.Zero)
            return false;

        IntPtr result = NativeMethods.SendMessage(listBoxHandle, NativeMethods.LB_SELECTSTRING, (IntPtr)(-1),
            new StringBuilder(itemText));
        return result.ToInt32() != NativeMethods.LB_ERR;
    }

    public bool ClickButton(string text)
    {
        if (_process == null || _process.HasExited || _handle == IntPtr.Zero)
            return false;

        IntPtr buttonHandle = GetControlHandle("Button", text);
        if (buttonHandle == IntPtr.Zero)
            return false;

        int buttonControlId = NativeMethods.GetDlgCtrlID(buttonHandle);
        NativeMethods.SendMessage(_handle, NativeMethods.WM_COMMAND, (IntPtr)buttonControlId, buttonHandle);

        return true;
    }

    public void WaitForExit()
    {
        if (_process == null || _process.HasExited)
            return;
        _process.WaitForExit();
    }

    #endregion

    #region Protected Fields

    private readonly Process _process = new();
    private IntPtr _handle = IntPtr.Zero;
    private IList<IntPtr> _controls = [];

    #endregion

    #region Protected Methods

    // ReSharper disable once UnusedMethodReturnValue.Local
    private IntPtr GetMainWindowHandle()
    {
        if (_process == null || _process.HasExited)
            return IntPtr.Zero;

        _process.WaitForInputIdle(Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000);

        _handle = IntPtr.Zero;
        int startTicks = Environment.TickCount;
        while (_handle == IntPtr.Zero &&
               Environment.TickCount < startTicks + (Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000))
        {
            _process.Refresh();
            _handle = _process.MainWindowHandle;
            if (_handle == IntPtr.Zero)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        return _handle;
    }

    private IntPtr GetControlHandle(string className, string text = "")
    {
        if (_process == null || _process.HasExited || _handle == IntPtr.Zero)
            return IntPtr.Zero;

        if (_controls.Count == 0)
        {
            EnumWindows windowEnumerator = new();
            _controls = windowEnumerator.EnumChildWindows(_handle);
        }

        StringBuilder stringBuilder = new();
        IntPtr controlHandle = IntPtr.Zero;
        foreach (IntPtr control in _controls)
        {
            _ = NativeMethods.GetClassName(control, stringBuilder, stringBuilder.Capacity);
            if (stringBuilder.ToString() != className) continue;
            if (string.IsNullOrEmpty(text))
            {
                controlHandle = control;
                break;
            }
            else
            {
                NativeMethods.SendMessage(control, NativeMethods.WM_GETTEXT, new IntPtr(stringBuilder.Capacity), stringBuilder);
                if (stringBuilder.ToString() != text) continue;
                controlHandle = control;
                break;
            }
        }

        return controlHandle;
    }

    #endregion

    private void Dispose(bool disposing)
    {
        if (!disposing) return;

        if(_process != null)
            _process.Dispose();

        _handle = IntPtr.Zero;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}