using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;

namespace mRemoteNG.Connection.Protocol.Winbox;

[SupportedOSPlatform("windows")]
public class ProtocolWinbox : ExternalProcessProtocolBase
{
    #region Private Fields

    private readonly ConnectionInfo _connectionInfo;
    private const string DefaultWinboxPath = "winbox.exe";
    private const string DefaultWinbox64Path = "winbox64.exe";

    #endregion

    #region Constructor

    public ProtocolWinbox(ConnectionInfo connectionInfo)
    {
        _connectionInfo = connectionInfo;
    }

    #endregion

    #region Public Methods

    public override bool Initialize()
    {
        return base.Initialize();
    }

    public override bool Connect()
    {
        try
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                "Attempting to start Winbox connection.", true);

            // Find Winbox executable
            string? winboxPath = FindWinboxExecutable();
            if (string.IsNullOrEmpty(winboxPath))
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.ErrorMsg,
                    "Winbox executable not found. Please ensure winbox.exe or winbox64.exe is in your PATH or in the same directory as mRemoteNG.", true);
                return false;
            }

            // Validate the executable path
            PathValidator.ValidateExecutablePathOrThrow(winboxPath, "Winbox");

            // Build arguments
            string arguments = BuildArguments();

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = winboxPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                },
                EnableRaisingEvents = true
            };

            _process.Exited += ProcessExited;
            _process.Start();

            // Wait for input idle (if applicable)
            try
            {
                _process.WaitForInputIdle(Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000);
            }
            catch (InvalidOperationException)
            {
                // Expected if Winbox behaves like a console app initially or exits quickly (wrapper)
            }

            int timeoutMs = Properties.OptionsAdvancedPage.Default.MaxPuttyWaitTime * 1000;
            int processId = _process.Id;

            // Strategy 1: Poll Process.MainWindowHandle
            _handle = PollMainWindowHandle(_process, timeoutMs);

            // Strategy 2: EnumWindows to find any visible top-level window owned by the process ID
            if (_handle == IntPtr.Zero)
            {
                _handle = FindWindowByProcessId(processId, timeoutMs);
            }

            // Strategy 3: Check child processes. WinBox may spawn a child process (e.g. for updates
            // or when using a single-instance wrapper) and the actual window belongs to the child.
            if (_handle == IntPtr.Zero)
            {
                _handle = FindWindowInChildProcesses(processId, timeoutMs);
            }

            if (_handle == IntPtr.Zero)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                    $"Winbox: Could not find a window handle for (PID {processId}). " +
                    "The application may have opened in a separate window or failed to start.");
                return false;
            }

            // If the window's actual owner process differs from the launched process (e.g. single-instance
            // WinBox forwarded args to an existing instance), track the correct process.
            _ = NativeMethods.GetWindowThreadProcessId(_handle, out uint windowPid);
            if (windowPid != (uint)processId)
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
                    Runtime.MessageCollector?.AddExceptionMessage("Winbox: Failed to attach to window owner process.", ex);
                }
            }

            // Reparent the window
            NativeMethods.SetParent(_handle, InterfaceControl.Handle);

            // Give keyboard focus to the embedded window after re-parenting.
            // Required to trigger a proper repaint — without this, WinBox renders as a gray window.
            NativeMethods.SetFocus(_handle);

            // Notify user
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg, Language.IntAppStuff, true);
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                string.Format(CultureInfo.InvariantCulture, Language.IntAppHandle, _handle), true);

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

    #endregion

    #region Private Methods

    private static string? FindWinboxExecutable()
    {
        // Check PATH
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable != null)
        {
            var paths = pathVariable.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var exePath = Path.Combine(path.Trim(), DefaultWinboxPath);
                if (File.Exists(exePath)) return exePath;

                exePath = Path.Combine(path.Trim(), DefaultWinbox64Path);
                if (File.Exists(exePath)) return exePath;
            }
        }

        // Check current directory
        if (File.Exists(DefaultWinboxPath)) return Path.GetFullPath(DefaultWinboxPath);
        if (File.Exists(DefaultWinbox64Path)) return Path.GetFullPath(DefaultWinbox64Path);

        return null;
    }

    private string BuildArguments()
    {
        // Winbox CLI: <address> <user> <password>
        // Winbox is lenient with arguments.
        string address = _connectionInfo.Hostname;
        string user = _connectionInfo.Username;
        string password = _connectionInfo.Password;

        // Handle port if specified in hostname or port field?
        // Usually Winbox uses address:port.
        if (_connectionInfo.Port > 0 && !address.Contains(':'))
        {
            address = $"{address}:{_connectionInfo.Port}";
        }

        return $"\"{address}\" \"{user}\" \"{password}\"";
    }

    #endregion
    #region Enumerations

    public enum Defaults
    {
        Port = 8291
    }

    #endregion
}