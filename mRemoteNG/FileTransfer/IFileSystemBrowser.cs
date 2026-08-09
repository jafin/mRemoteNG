using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// One side of the file manager: a filesystem that can be listed, navigated and modified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both panes implement this, so navigation, the list control and the transfer queue are written
    /// once rather than twice. The two sides genuinely differ — path syntax, permissions, what
    /// "hidden" means — and those differences live in the implementations.
    /// </para>
    /// <para>
    /// Every operation is asynchronous. A local listing of a large directory on a slow disk, or a
    /// remote listing over a slow link, would freeze the tab if run on the UI thread.
    /// </para>
    /// </remarks>
    public interface IFileSystemBrowser
    {
        /// <summary>Where navigation starts, and where the home button goes.</summary>
        string HomePath { get; }

        /// <summary>Whether entries carry meaningful permission strings.</summary>
        bool SupportsPermissions { get; }

        /// <summary>The path separator this filesystem uses, for display.</summary>
        char DirectorySeparator { get; }

        Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>The parent of <paramref name="path"/>, or the path itself when it has no parent.</summary>
        string GetParentPath(string path);

        /// <summary>Joins a directory and an entry name.</summary>
        string Combine(string directory, string name);

        Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default);

        Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default);

        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        Task CreateFileAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Opens <paramref name="path"/> for reading, as the source of a transfer.</summary>
        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes <paramref name="source"/> to <paramref name="path"/>, replacing anything there.
        /// </summary>
        /// <param name="totalBytes">
        /// The expected size, for progress reporting. Supplied by the caller because a non-seekable
        /// source cannot report one.
        /// </param>
        Task WriteAsync(Stream source,
                        string path,
                        long? totalBytes = null,
                        System.IProgress<long>? bytesTransferred = null,
                        CancellationToken cancellationToken = default);
    }
}
