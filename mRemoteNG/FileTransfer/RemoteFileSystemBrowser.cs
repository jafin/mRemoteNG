using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// The remote side of the file manager, over an <see cref="ISftpSession"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class RemoteFileSystemBrowser : IFileSystemBrowser
    {
        private readonly ISftpSession _session;

        public RemoteFileSystemBrowser(ISftpSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            _session = session;
        }

        public string HomePath => _session.HomeDirectory;

        public bool SupportsPermissions => true;

        public char DirectorySeparator => '/';

        public async Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SftpEntry> entries =
                await _session.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false);

            List<FileSystemEntry> mapped = new(entries.Count);
            foreach (SftpEntry entry in entries)
                mapped.Add(Describe(entry));

            return mapped;
        }

        public string GetParentPath(string path) => SftpPath.GetParent(path);

        public string Combine(string directory, string name) => SftpPath.Combine(directory, name);

        public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
            _session.RenameAsync(fromPath, toPath, cancellationToken);

        public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return _session.DeleteAsync(ToSftpEntry(entry), cancellationToken);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            _session.CreateDirectoryAsync(path, cancellationToken);

        public Task CreateFileAsync(string path, CancellationToken cancellationToken = default) =>
            _session.CreateFileAsync(path, cancellationToken);

        /// <summary>
        /// Buffers the remote file in memory and hands back a stream over it.
        /// </summary>
        /// <remarks>
        /// <c>SftpClient</c> exposes downloading as "write into a stream I give you", which is the
        /// opposite shape from <see cref="IFileSystemBrowser.OpenReadAsync"/>. Buffering bridges the
        /// two, at the cost of holding the file in memory — acceptable for the edit-a-file path, and
        /// the reason the transfer queue does <b>not</b> use this: it pairs
        /// <c>OpenReadAsync</c> on the source with <c>WriteAsync</c> on the destination only for
        /// local sources, and calls the session's own download directly for remote ones.
        /// </remarks>
        public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);

            SftpEntry entry = new(SftpPath.GetName(path), path, IsDirectory: false, IsSymbolicLink: false,
                                  Length: 0, LastWriteTime: DateTime.Now, Permissions: string.Empty);

            MemoryStream buffer = new();
            await _session.DownloadAsync(entry, buffer, progress: null, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }

        public Task WriteAsync(Stream source,
                               string path,
                               long? totalBytes = null,
                               IProgress<long>? bytesTransferred = null,
                               CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(path);

            IProgress<SftpTransferProgress>? progress = bytesTransferred is null
                ? null
                : new Progress<SftpTransferProgress>(p => bytesTransferred.Report(p.Transferred));

            return _session.UploadAsync(source, path, totalBytes, progress, cancellationToken);
        }

        internal static FileSystemEntry Describe(SftpEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return new FileSystemEntry(
                Name: entry.Name,
                FullPath: entry.FullName,
                IsDirectory: entry.IsDirectory,
                Length: entry.Length,
                LastWriteTime: entry.LastWriteTime,
                Permissions: entry.Permissions,
                IsHidden: entry.IsHidden);
        }

        /// <summary>
        /// Rebuilds the SFTP entry a delete needs. Only the path and the directory flag matter —
        /// they are what decide between removing a file and removing a directory.
        /// </summary>
        private static SftpEntry ToSftpEntry(FileSystemEntry entry) =>
            new(entry.Name, entry.FullPath, entry.IsDirectory, IsSymbolicLink: false,
                entry.Length, entry.LastWriteTime, entry.Permissions);
    }
}
