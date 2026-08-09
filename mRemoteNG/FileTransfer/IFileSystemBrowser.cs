using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer;

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

    /// <summary>
    /// Whether two names differing only in case name different entries.
    /// </summary>
    /// <remarks>
    /// A property of the filesystem, not of the caller. It decides whether uploading
    /// <c>Readme.md</c> onto a server that already holds <c>README.md</c> is a collision or a
    /// second file, and getting it wrong in either direction loses data or duplicates it.
    /// </remarks>
    bool PathsAreCaseSensitive { get; }

    Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The parent of <paramref name="path"/>, or the path itself when it has no parent.</summary>
    string GetParentPath(string path);

    /// <summary>Joins a directory and an entry name.</summary>
    string Combine(string directory, string name);

    Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default);

    Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates <paramref name="path"/> if it is not already there, and succeeds either way.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the directory had to be created, <see langword="false"/> when it
    /// already existed. The distinction matters to a recursive transfer: a directory it just
    /// created is empty, so nothing inside it can collide and it need not be listed.
    /// </returns>
    Task<bool> EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task CreateFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Follows the link at <paramref name="path"/> and reports whether it lands on a directory.
    /// </summary>
    /// <remarks>
    /// A listing cannot answer this. It reports the link itself — SFTP lists with the equivalent of
    /// <c>lstat</c> — so a link to a directory and a link to a file are indistinguishable in it.
    /// Only a following stat separates them, which is why this exists and why it is called for
    /// links alone.
    /// </remarks>
    Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken cancellationToken = default);

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