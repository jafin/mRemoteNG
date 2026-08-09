using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;

namespace mRemoteNG.Connection.Protocol.VMRC;

[SupportedOSPlatform("windows")]
public class ProtocolVMRC : ExternalProcessProtocolBase
{
    private readonly ConnectionInfo _connectionInfo;

    private static readonly string[] DefaultVmwareViewExecutables =
    [
        @"C:\Program Files (x86)\VMware\VMware Horizon View Client\vmware-view.exe",
        @"C:\Program Files\VMware\VMware Horizon View Client\vmware-view.exe"
    ];

    public ProtocolVMRC(ConnectionInfo connectionInfo)
    {
        _connectionInfo = connectionInfo;
    }

    public override bool Connect()
    {
        try
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                "Attempting to start VMware Horizon View Client.", true);

            string serverUrl = _connectionInfo.Hostname?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(serverUrl))
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.ErrorMsg,
                    "Server URL is required for VMRC protocol.");
                return false;
            }

            string? vmwareViewPath = FindVmwareViewExecutable();
            if (string.IsNullOrEmpty(vmwareViewPath))
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.ErrorMsg,
                    "VMware Horizon View Client executable (vmware-view.exe) was not found.");
                return false;
            }

            PathValidator.ValidateExecutablePathOrThrow(vmwareViewPath, nameof(vmwareViewPath));

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = vmwareViewPath,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            foreach (string argument in BuildArguments(serverUrl,
                         _connectionInfo.VmId,
                         _connectionInfo.Username,
                         _connectionInfo.Domain,
                         _connectionInfo.Password))
            {
                _process.StartInfo.ArgumentList.Add(argument);
            }

            _process.Exited += ProcessExited;
            _process.Start();

            try
            {
                _process.WaitForInputIdle(Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000);
            }
            catch (InvalidOperationException)
            {
                // Some GUI apps may not expose an input loop immediately.
            }

            int timeoutMs = Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000;
            int processId = _process.Id;

            _handle = PollMainWindowHandle(_process, timeoutMs);
            if (_handle == IntPtr.Zero)
            {
                _handle = FindWindowByProcessId(processId, timeoutMs);
            }

            if (_handle == IntPtr.Zero)
            {
                _handle = FindWindowInChildProcesses(processId, timeoutMs);
            }

            if (_handle == IntPtr.Zero)
            {
                _handle = FindWindowInDescendantProcesses(processId, timeoutMs, 4);
            }

            if (_handle == IntPtr.Zero)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                    "VMRC: Could not find the Horizon View client window. The connection will close.");
                return false;
            }

            _ = NativeMethods.GetWindowThreadProcessId(_handle, out uint windowPid);
            if (windowPid != (uint)_process.Id)
            {
                try
                {
                    Process windowProcess = Process.GetProcessById((int)windowPid);
                    _process.Exited -= ProcessExited;
                    _process = windowProcess;
                    _process.EnableRaisingEvents = true;
                    _process.Exited += ProcessExited;
                }
                catch (Exception ex)
                {
                    Runtime.MessageCollector?.AddExceptionMessage("VMRC: Failed to attach process to window owner.", ex);
                }
            }

            NativeMethods.SetParent(_handle, InterfaceControl.Handle);
            NativeMethods.SetFocus(_handle);

            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg, Language.IntAppStuff, true);
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                string.Format(CultureInfo.InvariantCulture, Language.IntAppHandle, _handle), true);
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                string.Format(CultureInfo.InvariantCulture, Language.IntAppTitle, _process.MainWindowTitle), true);

            Resize(this, EventArgs.Empty);
            base.Connect();
            return true;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector?.AddExceptionMessage(Language.ConnectionFailed, ex);
            return false;
        }
    }

    public override void Focus()
    {
        try
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(_handle);
                NativeMethods.SetFocus(_handle);
            }
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector?.AddExceptionMessage(Language.IntAppFocusFailed, ex);
        }
    }

    private static List<string> BuildArguments(
        string serverUrl,
        string? desktopName,
        string? userName,
        string? domainName,
        string? password)
    {
        List<string> arguments = [];
        arguments.Add("-serverURL");
        arguments.Add(serverUrl);

        AddArgument(arguments, "-desktopName", desktopName);
        AddArgument(arguments, "-userName", userName);
        AddArgument(arguments, "-domainName", domainName);
        AddArgument(arguments, "-password", password);

        return arguments;
    }

    private static void AddArgument(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        arguments.Add(name);
        arguments.Add(value.Trim());
    }

    private static string? FindVmwareViewExecutable()
    {
        foreach (string path in DefaultVmwareViewExecutables)
        {
            if (File.Exists(path))
                return path;
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable != null)
        {
            foreach (string path in pathVariable.Split(Path.PathSeparator))
            {
                string trimmedPath = path.Trim();
                if (trimmedPath.Length == 0)
                    continue;

                string filePath = Path.Combine(trimmedPath, "vmware-view.exe");
                if (File.Exists(filePath))
                    return filePath;
            }
        }

        return null;
    }

    public enum Defaults
    {
        Port = 0
    }
}