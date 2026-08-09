using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using Renci.SshNet;
using SshNetConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace mRemoteNG.Tools;

/// <summary>Progress of an in-flight upload.</summary>
/// <param name="transferred">Bytes sent so far.</param>
/// <param name="total">Total bytes, or -1 when the size is not known.</param>
internal sealed class SecureTransferProgressEventArgs(long transferred, long total) : EventArgs
{
    public long Transferred { get; } = transferred;

    public long Total { get; } = total;
}

[SupportedOSPlatform("windows")]
internal sealed class SecureTransfer : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ResolvedSshCredential _credential;
    private SshNetAuthentication? _authentication;

    public readonly SshTransferProtocol Protocol;
    public string SrcFile = string.Empty;
    public string DstFile = string.Empty;
    public ScpClient? ScpClt;
    public SftpClient? SftpClt;

    /// <summary>
    /// Raised as bytes go out, for both protocols. SCP reports through SSH.NET's own upload
    /// event; SFTP reports from the source stream, since <c>UploadFileAsync</c> has no progress
    /// callback.
    /// </summary>
    public event EventHandler<SecureTransferProgressEventArgs>? UploadProgress;

    /// <param name="credential">
    /// Ownership passes to this instance: it is disposed with the transfer, after the upload
    /// has finished with it. The authentication methods read it lazily, so it must stay alive
    /// for the whole connection.
    /// </param>
    public SecureTransfer(string host,
        int port,
        ResolvedSshCredential credential,
        SshTransferProtocol protocol,
        string source = "",
        string dest = "")
    {
        ArgumentNullException.ThrowIfNull(credential);

        _host = host;
        _port = port;
        _credential = credential;
        Protocol = protocol;
        SrcFile = source.Trim('"');
        DstFile = dest.Trim('"');
    }

    /// <summary>
    /// Builds the authentication methods and the protocol client without any network I/O, so
    /// what a given credential turns into is testable without a server.
    /// </summary>
    internal void CreateClient()
    {
        _authentication = SshNetAuthAdapter.Translate(_credential);

        foreach (SshCredentialDiagnostic diagnostic in _credential.Diagnostics)
        {
            Runtime.MessageCollector?.AddMessage(
                diagnostic.Severity == SshCredentialDiagnosticSeverity.Information
                    ? MessageClass.InformationMsg
                    : MessageClass.ErrorMsg,
                diagnostic.Message);
        }

        foreach (SshCredentialDiagnostic diagnostic in _authentication.Unsupported)
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg, diagnostic.Message);
        }

        SshNetConnectionInfo connectionInfo =
            new(_host, _port, _authentication.Username, _authentication.Methods);

        if (Protocol == SshTransferProtocol.Scp)
        {
            ScpClt = new ScpClient(connectionInfo);
            ScpClt.Uploading += OnScpUploading;
        }
        else
        {
            SftpClt = new SftpClient(connectionInfo);
        }
    }

    public void Connect()
    {
        CreateClient();

        try
        {
            if (Protocol == SshTransferProtocol.Scp)
                ScpClt?.Connect();
            else
                SftpClt?.Connect();
        }
        catch
        {
            ReportUnansweredPrompts();
            throw;
        }
    }

    public void Disconnect()
    {
        if (Protocol == SshTransferProtocol.Scp)
        {
            ScpClt?.Disconnect();
        }

        if (Protocol == SshTransferProtocol.Sftp)
        {
            SftpClt?.Disconnect();
        }
    }

    /// <summary>
    /// Uploads <see cref="SrcFile"/> to <see cref="DstFile"/>.
    /// </summary>
    /// <remarks>
    /// The two protocols are deliberately asymmetric. SFTP awaits SSH.NET's native
    /// <c>UploadFileAsync</c>; SCP runs synchronously because <c>ScpClient</c> exposes no async
    /// method in SSH.NET 2025.1.0, so the SCP path completes before the first await.
    /// </remarks>
    public async Task UploadAsync(CancellationToken cancellationToken = default)
    {
        if (Protocol == SshTransferProtocol.Scp)
        {
            if (ScpClt is null || !ScpClt.IsConnected)
            {
                ReportNotConnected("SCP");
                return;
            }

            ScpClt.Upload(new FileInfo(SrcFile), DstFile);
            return;
        }

        if (SftpClt is null || !SftpClt.IsConnected)
        {
            ReportNotConnected("SFTP");
            return;
        }

        FileStream source = new(SrcFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (source.ConfigureAwait(false))
        {
            ProgressReportingStream progress = new(source, ReportProgress);
            await using (progress.ConfigureAwait(false))
            {
                await SftpClt.UploadFileAsync(progress, DstFile, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public enum SshTransferProtocol
    {
        Scp = 0,
        Sftp = 1
    }

    private void OnScpUploading(object? sender, Renci.SshNet.Common.ScpUploadEventArgs e) =>
        ReportProgress((long)e.Uploaded, e.Size);

    private void ReportProgress(long transferred, long total) =>
        UploadProgress?.Invoke(this, new SecureTransferProgressEventArgs(transferred, total));

    private static void ReportNotConnected(string protocol) =>
        Runtime.MessageCollector?.AddMessage(MessageClass.ErrorMsg,
            Language.SshTransferFailed + Environment.NewLine + $"{protocol} Not Connected!");

    /// <summary>
    /// Names any keyboard-interactive question the server asked that nothing could answer —
    /// almost always a second factor. Without this, a connection that fails on an unanswerable
    /// prompt reports only that authentication failed.
    /// </summary>
    private void ReportUnansweredPrompts()
    {
        if (_authentication is null)
            return;

        foreach (string prompt in _authentication.UnansweredPrompts)
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                $"The server asked \"{prompt.Trim()}\" during keyboard-interactive " +
                "authentication and mRemoteNG had no answer for it. Connections needing a " +
                "second factor cannot be completed by this transfer window.");
        }
    }

    public void Dispose()
    {
        if (ScpClt is not null)
            ScpClt.Uploading -= OnScpUploading;

        ScpClt?.Dispose();
        SftpClt?.Dispose();
        _authentication?.Dispose();
        _credential.Dispose();
        GC.SuppressFinalize(this);
    }
}