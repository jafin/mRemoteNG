using System;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// One entry in either pane's listing.
    /// </summary>
    /// <remarks>
    /// Common to the local and remote sides so a single list control serves both. Permissions are
    /// remote-only — the local pane leaves them empty rather than inventing a rendering of Windows
    /// ACLs that would not mean the same thing.
    /// </remarks>
    /// <param name="Name">The entry name within its directory.</param>
    /// <param name="FullPath">The full path, in whichever filesystem's own syntax.</param>
    /// <param name="IsDirectory">Whether the entry is a directory.</param>
    /// <param name="Length">Size in bytes. Directories report 0.</param>
    /// <param name="LastWriteTime">Last modification time.</param>
    /// <param name="Permissions">A <c>drwxr-xr-x</c> rendering, or empty where it does not apply.</param>
    /// <param name="IsHidden">Whether the filesystem considers this entry hidden.</param>
    public sealed record FileSystemEntry(
        string Name,
        string FullPath,
        bool IsDirectory,
        long Length,
        DateTime LastWriteTime,
        string Permissions,
        bool IsHidden);
}
