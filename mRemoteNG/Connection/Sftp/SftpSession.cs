using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using mRemoteNG.Tools;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SshNetConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace mRemoteNG.Connection.Sftp
{
    /// <summary>
    /// An <see cref="ISftpSession"/> backed by SSH.NET.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opens its own SSH connection rather than sharing the session's. Sharing is not available:
    /// SSH.NET's <c>ISession</c> is <c>internal</c>, and <c>ssh.exe</c> cannot multiplex on Windows
    /// — <c>ControlMaster</c> parses and is echoed by <c>ssh -G</c> but the socket layer is absent,
    /// so it fails silently. See <c>add-sftp-browser-panel</c> design.md D1.
    /// </para>
    /// <para>
    /// The second connection means a second authentication. With an SSH agent or a stored credential
    /// that is silent; with a server-issued second factor it is a second prompt, and there is no way
    /// around that without transport sharing.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class SftpSession : ISftpSession
    {
        private readonly string _host;
        private readonly int _port;
        private readonly ResolvedSshCredential _credential;

        private SftpClient? _client;
        private SshNetAuthentication? _authentication;
        private bool _disposed;

        /// <param name="credential">
        /// Ownership passes to this instance. The authentication methods read it lazily, so it must
        /// stay alive for the life of the connection.
        /// </param>
        public SftpSession(string host, int port, ResolvedSshCredential credential)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(credential);

            _host = host;
            _port = port;
            _credential = credential;
        }

        /// <summary>
        /// Builds a session for an mRemoteNG connection, resolving its credentials the same way
        /// every other SSH.NET-backed caller does.
        /// </summary>
        public static SftpSession ForConnection(ConnectionInfo connectionInfo, ISshAgentSettingsSource? agentSettings = null)
        {
            ArgumentNullException.ThrowIfNull(connectionInfo);

            bool agentEnabled = (agentSettings ?? DefaultAgentSettings.Instance).IsEnabled;

            ResolvedSshCredential credential = SshCredentialResolver
                .CreateDefault()
                .Resolve(connectionInfo, SshCredentialResolutionOptions.ForSshNet(agentEnabled));

            return new SftpSession(connectionInfo.Hostname.Trim(), connectionInfo.Port, credential);
        }

        public bool IsConnected => _client?.IsConnected == true;

        public string HomeDirectory { get; private set; } = SftpPath.Root;

        public event EventHandler<string>? Dropped;

        public IReadOnlyList<SshCredentialDiagnostic> Diagnostics { get; private set; } = [];

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _authentication = SshNetAuthAdapter.Translate(_credential);

            List<SshCredentialDiagnostic> diagnostics = [.. _credential.Diagnostics, .. _authentication.Unsupported];
            Diagnostics = diagnostics;

            SshNetConnectionInfo connectionInfo =
                new(_host, _port, _authentication.Username, _authentication.Methods);

            SftpClient client = new(connectionInfo);
            client.ErrorOccurred += OnClientError;

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.ErrorOccurred -= OnClientError;
                client.Dispose();
                throw;
            }

            _client = client;
            HomeDirectory = SftpPath.Normalize(client.WorkingDirectory);
        }

        public async Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            SftpClient client = RequireConnected();

            List<SftpEntry> entries = [];

            await foreach (ISftpFile file in client.ListDirectoryAsync(SftpPath.Normalize(path), cancellationToken)
                                                   .ConfigureAwait(false))
            {
                SftpEntry entry = Describe(file);

                // Servers include these; the panel navigates with its own controls.
                if (entry.IsCurrentOrParentDirectory)
                    continue;

                entries.Add(entry);
            }

            return entries;
        }

        public async Task DownloadAsync(SftpEntry file,
                                        Stream destination,
                                        IProgress<SftpTransferProgress>? progress = null,
                                        CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(destination);
            SftpClient client = RequireConnected();

            // DownloadFileAsync has no progress callback, so progress is counted from the stream it
            // writes into. The total comes from the listing, since the destination is a new file
            // whose length says nothing about the size of the transfer.
            ProgressReportingStream counted = new(destination, Reporter(progress), file.Length);
            await using (counted.ConfigureAwait(false))
            {
                await client.DownloadFileAsync(file.FullName, counted, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task UploadAsync(Stream source,
                                      string remotePath,
                                      long? totalBytes = null,
                                      IProgress<SftpTransferProgress>? progress = null,
                                      CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(remotePath);
            SftpClient client = RequireConnected();

            ProgressReportingStream counted = new(source, Reporter(progress), totalBytes);
            await using (counted.ConfigureAwait(false))
            {
                await client.UploadFileAsync(counted, SftpPath.Normalize(remotePath), cancellationToken)
                            .ConfigureAwait(false);
            }
        }

        public async Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fromPath);
            ArgumentNullException.ThrowIfNull(toPath);
            SftpClient client = RequireConnected();

            await client.RenameFileAsync(SftpPath.Normalize(fromPath), SftpPath.Normalize(toPath), cancellationToken)
                        .ConfigureAwait(false);
        }

        public async Task DeleteAsync(SftpEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            SftpClient client = RequireConnected();

            // A non-empty directory fails here, and that is the right outcome: the panel reports the
            // server's refusal rather than quietly recursing through a tree the user did not see.
            if (entry.IsDirectory && !entry.IsSymbolicLink)
                await client.DeleteDirectoryAsync(entry.FullName, cancellationToken).ConfigureAwait(false);
            else
                await client.DeleteFileAsync(entry.FullName, cancellationToken).ConfigureAwait(false);
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            SftpClient client = RequireConnected();

            await client.CreateDirectoryAsync(SftpPath.Normalize(path), cancellationToken).ConfigureAwait(false);
        }

        public async Task CreateFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            SftpClient client = RequireConnected();

            // Uploading nothing is the async way to create an empty file; SftpClient.Create is
            // synchronous only.
            using MemoryStream empty = new([]);
            await client.UploadFileAsync(empty, SftpPath.Normalize(path), cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            SftpClient client = RequireConnected();

            return await client.ExistsAsync(SftpPath.Normalize(path), cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ResolvesToDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            SftpClient client = RequireConnected();

            try
            {
                SftpFileAttributes attributes = await client
                    .GetAttributesAsync(SftpPath.Normalize(path), cancellationToken)
                    .ConfigureAwait(false);

                return attributes.IsDirectory;
            }
            catch (SftpPathNotFoundException)
            {
                // A link whose target does not exist. Not a directory, and not worth failing over:
                // the caller only wants to know whether it is safe to descend.
                return false;
            }
        }

        /// <summary>
        /// Maps a library file entry onto the neutral model. Internal so the mapping is testable
        /// against a faked <see cref="ISftpFile"/> rather than requiring a server.
        /// </summary>
        internal static SftpEntry Describe(ISftpFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            bool isDirectory = file.IsDirectory;

            return new SftpEntry(
                Name: file.Name,
                FullName: SftpPath.Normalize(file.FullName),
                IsDirectory: isDirectory,
                IsSymbolicLink: file.IsSymbolicLink,
                Length: isDirectory ? 0 : file.Length,
                LastWriteTime: file.LastWriteTime,
                Permissions: FormatPermissions(file));
        }

        /// <summary>
        /// Renders the mode bits the way <c>ls -l</c> does. SFTP exposes them only as individual
        /// booleans, so the familiar form has to be assembled.
        /// </summary>
        internal static string FormatPermissions(ISftpFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            StringBuilder builder = new(10);

            builder.Append(file.IsSymbolicLink ? 'l'
                         : file.IsDirectory ? 'd'
                         : file.IsBlockDevice ? 'b'
                         : file.IsCharacterDevice ? 'c'
                         : file.IsNamedPipe ? 'p'
                         : file.IsSocket ? 's'
                         : '-');

            builder.Append(file.OwnerCanRead ? 'r' : '-');
            builder.Append(file.OwnerCanWrite ? 'w' : '-');
            builder.Append(file.OwnerCanExecute ? 'x' : '-');
            builder.Append(file.GroupCanRead ? 'r' : '-');
            builder.Append(file.GroupCanWrite ? 'w' : '-');
            builder.Append(file.GroupCanExecute ? 'x' : '-');
            builder.Append(file.OthersCanRead ? 'r' : '-');
            builder.Append(file.OthersCanWrite ? 'w' : '-');
            builder.Append(file.OthersCanExecute ? 'x' : '-');

            return builder.ToString();
        }

        private static Action<long, long> Reporter(IProgress<SftpTransferProgress>? progress) =>
            progress is null
                ? static (_, _) => { }
                : (transferred, total) => progress.Report(new SftpTransferProgress(transferred, total));

        /// <summary>
        /// Returns the client, or throws if the session is not usable.
        /// </summary>
        /// <remarks>
        /// Failing here is deliberate. A listing served from a dropped session would be presented as
        /// the current state of the remote host when it is not, which is worse than an error.
        /// </remarks>
        private SftpClient RequireConnected()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_client is null || !_client.IsConnected)
                throw new SftpSessionNotConnectedException();

            return _client;
        }

        private void OnClientError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e) =>
            Dropped?.Invoke(this, e.Exception?.Message ?? string.Empty);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_client is not null)
            {
                _client.ErrorOccurred -= OnClientError;
                _client.Dispose();
                _client = null;
            }

            _authentication?.Dispose();
            _credential.Dispose();
        }

        /// <summary>Indirection over the global agent setting, so callers can be tested.</summary>
        public interface ISshAgentSettingsSource
        {
            bool IsEnabled { get; }
        }

        private sealed class DefaultAgentSettings : ISshAgentSettingsSource
        {
            public static readonly DefaultAgentSettings Instance = new();

            public bool IsEnabled => Security.Ssh.Agent.SshAgentSettings.Default.IsEnabled;
        }
    }
}
