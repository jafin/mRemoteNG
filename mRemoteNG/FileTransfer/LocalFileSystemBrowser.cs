using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Tools;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// The local side of the file manager.
    /// </summary>
    /// <remarks>
    /// Enumeration runs on a worker thread. <see cref="Directory"/> is synchronous throughout, and a
    /// large directory, a slow disk or a disconnected network drive would otherwise freeze the tab.
    /// </remarks>
    public class LocalFileSystemBrowser : IFileSystemBrowser
    {
        public LocalFileSystemBrowser(string? homePath = null)
        {
            HomePath = string.IsNullOrEmpty(homePath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : homePath;
        }

        public string HomePath { get; }

        /// <summary>
        /// Always false. Windows ACLs do not map onto the <c>rwxr-xr-x</c> rendering the remote pane
        /// shows, and a partial translation would be more misleading than an empty column.
        /// </summary>
        public bool SupportsPermissions => false;

        public char DirectorySeparator => Path.DirectorySeparatorChar;

        /// <summary>
        /// False. NTFS can be configured otherwise per directory, but Windows presents itself as
        /// case-insensitive and treating it as sensitive would let a download quietly replace
        /// <c>README.md</c> with <c>Readme.md</c> while reporting no collision.
        /// </summary>
        public bool PathsAreCaseSensitive => false;

        public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);

            return Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
            {
                List<FileSystemEntry> entries = [];
                DirectoryInfo directory = new(path);

                // Enumerated rather than materialised as arrays: a directory with tens of thousands
                // of entries should start producing results without building two full arrays first.
                foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Add(Describe(info));
                }

                return entries;
            }, cancellationToken);
        }

        public string GetParentPath(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            // A drive root has no parent, and Path.GetDirectoryName returns null for one. Returning
            // the path itself keeps repeated "up" presses terminating rather than throwing.
            return Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                   ?? path;
        }

        public string Combine(string directory, string name)
        {
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(name);

            return Path.Combine(directory, name);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fromPath);
            ArgumentNullException.ThrowIfNull(toPath);

            return Task.Run(() =>
            {
                if (Directory.Exists(fromPath))
                    Directory.Move(fromPath, toPath);
                else
                    File.Move(fromPath, toPath);
            }, cancellationToken);
        }

        public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return Task.Run(() =>
            {
                // Non-recursive on purpose, matching the remote side: deleting a tree the user has
                // not seen is not something a delete button should do silently.
                if (entry.IsDirectory)
                    Directory.Delete(entry.FullPath, recursive: false);
                else
                    File.Delete(entry.FullPath);
            }, cancellationToken);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Task.Run(() => Directory.CreateDirectory(path), cancellationToken);
        }

        /// <summary>
        /// Creates the directory if it is missing, reporting whether it had to.
        /// </summary>
        /// <remarks>
        /// <see cref="Directory.CreateDirectory(string)"/> is already idempotent, so the only work here
        /// is answering "did it exist" — which the caller needs, and which must be asked before the
        /// create rather than after.
        /// </remarks>
        public Task<bool> EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);

            return Task.Run(() =>
            {
                bool existed = Directory.Exists(path);
                Directory.CreateDirectory(path);
                return !existed;
            }, cancellationToken);
        }

        /// <summary>
        /// Whether the reparse point at <paramref name="path"/> lands on a directory.
        /// </summary>
        /// <remarks>
        /// <see cref="Directory.Exists(string)"/> follows a reparse point on Windows, so it answers the
        /// question directly. A broken link returns false, which is the right answer for a caller
        /// deciding whether it is safe to descend.
        /// </remarks>
        public Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Task.Run(() => Directory.Exists(path), cancellationToken);
        }

        public Task CreateFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);

            return Task.Run(() =>
            {
                // CreateNew rather than Create: silently truncating a file the user already has is
                // not what "new file" means.
                using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);

            return Task.Run<Stream>(
                () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                cancellationToken);
        }

        public async Task WriteAsync(Stream source,
                                     string path,
                                     long? totalBytes = null,
                                     IProgress<long>? bytesTransferred = null,
                                     CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(path);

            FileStream destination = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (destination.ConfigureAwait(false))
            {
                ProgressReportingStream counted =
                    new(destination, (transferred, _) => bytesTransferred?.Report(transferred), totalBytes);

                await using (counted.ConfigureAwait(false))
                {
                    await source.CopyToAsync(counted, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        internal static FileSystemEntry Describe(FileSystemInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);

            bool isDirectory = info is DirectoryInfo;

            return new FileSystemEntry(
                Name: info.Name,
                FullPath: info.FullName,
                IsDirectory: isDirectory,
                Length: isDirectory ? 0 : SafeLength(info),
                LastWriteTime: info.LastWriteTime,
                Permissions: string.Empty,
                IsHidden: info.Attributes.HasFlag(FileAttributes.Hidden),
                IsSymbolicLink: info.Attributes.HasFlag(FileAttributes.ReparsePoint));
        }

        /// <summary>
        /// A file can vanish between being enumerated and being measured, and a listing should not
        /// fail because one entry disappeared while it was being read.
        /// </summary>
        private static long SafeLength(FileSystemInfo info)
        {
            try
            {
                return info is FileInfo file ? file.Length : 0;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
