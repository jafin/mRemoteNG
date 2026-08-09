using System;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;

namespace mRemoteNG.Connection.Protocol.SSH;

[SupportedOSPlatform("windows")]
public class ProtocolOpenSSH(ConnectionInfo connectionInfo) : ExternalProcessProtocolBase
{
    #region Private Fields

    private readonly ConnectionInfo _connectionInfo = connectionInfo;
    private ConsoleControl.ConsoleControl? _consoleControl;

    #endregion

    #region Public Methods

    public override bool Connect()
    {
        try
        {
            string? sshExe = FindSshExe();
            if (sshExe == null)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.ErrorMsg,
                    "Windows OpenSSH client (ssh.exe) was not found. " +
                    "Please install the OpenSSH Client optional feature via Settings > Apps > Optional Features.", true);
                return false;
            }

            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                $"Attempting to start OpenSSH session using {sshExe}.", true);

            _consoleControl = new ConsoleControl.ConsoleControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ForeColor = Color.White,
                IsInputEnabled = true,
                Padding = new Padding(0, 20, 0, 0)
            };

            string arguments = BuildSshArguments();
            _consoleControl.StartProcess(sshExe, arguments);

            // Wait for the console control to create its handle
            int maxWaitMs = 5000;
            long startTicks = Environment.TickCount64;
            while (!_consoleControl.IsHandleCreated &&
                   Environment.TickCount64 < startTicks + maxWaitMs)
            {
                System.Threading.Thread.Sleep(50);
            }

            if (!_consoleControl.IsHandleCreated)
            {
                throw new TimeoutException("Failed to initialize OpenSSH console within 5 seconds.");
            }

            _handle = _consoleControl.Handle;
            NativeMethods.SetParent(_handle, InterfaceControl.Handle);

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

    private static string? FindSshExe()
    {
        // Try the standard Windows OpenSSH location first
        string systemSsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "OpenSSH", "ssh.exe");

        if (File.Exists(systemSsh))
            return systemSsh;

        // Fallback: try to find ssh.exe on PATH
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar != null)
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir.Trim(), "ssh.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves this connection's SSH credentials. Overridable so tests do not depend on the
    /// developer's <c>~/.ssh</c> or on live credential providers.
    /// </summary>
    protected virtual ResolvedSshCredential ResolveCredentials()
    {
        return SshCredentialResolver.CreateDefault()
            .Resolve(_connectionInfo, SshCredentialResolutionOptions.ForOpenSsh);
    }

    private string BuildSshArguments()
    {
        string hostname = _connectionInfo.Hostname.Trim();
        int port = _connectionInfo.Port;

        using ResolvedSshCredential credential = ResolveCredentials();

        foreach (SshCredentialDiagnostic diagnostic in credential.Diagnostics)
        {
            Runtime.MessageCollector?.AddMessage(
                diagnostic.Severity == SshCredentialDiagnosticSeverity.Information
                    ? MessageClass.InformationMsg
                    : MessageClass.ErrorMsg,
                diagnostic.Message);
        }

        OpenSshCredentialArguments credentialArgs = OpenSshArgsAdapter.Translate(credential, hostname);

        // ssh.exe cannot take a password non-interactively. Previously the resolved secret was
        // dropped silently and the user got an unexplained console prompt; now it is named.
        foreach (SshCredentialDiagnostic unsupported in credentialArgs.Unsupported)
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg, unsupported.Message);
        }

        string args = "";

        // Add port if not default
        if (port > 0 && port != 22)
        {
            args += $"-p {port} ";
        }

        // Add SSH options (extra flags like -o StrictHostKeyChecking=no)
        string sshOptions = _connectionInfo.SSHOptions?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(sshOptions))
        {
            args += $"{sshOptions} ";
        }

        if (!string.IsNullOrEmpty(credentialArgs.IdentityArgument))
        {
            if (!string.Equals(credential.PrivateKeyPath, _connectionInfo.PrivateKeyPath?.Trim(), StringComparison.Ordinal))
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                    $"No private key configured; auto-discovered SSH key: {credential.PrivateKeyPath}", true);
            }

            args += credentialArgs.IdentityArgument + " ";
        }

        args += credentialArgs.Destination;

        return args.Trim();
    }

    #endregion

    #region Enumerations

    public enum Defaults
    {
        Port = 22
    }

    #endregion
}