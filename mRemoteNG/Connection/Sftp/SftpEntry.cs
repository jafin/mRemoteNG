using System;

namespace mRemoteNG.Connection.Sftp
{
    /// <summary>
    /// One entry in a remote directory listing.
    /// </summary>
    /// <remarks>
    /// Carries no SSH.NET types. The panel binds to this, so the list can be populated in tests
    /// without a server and without faking the SFTP library's own interfaces.
    /// </remarks>
    /// <param name="Name">The entry name within its directory.</param>
    /// <param name="FullName">The absolute remote path.</param>
    /// <param name="IsDirectory">Whether the entry is a directory.</param>
    /// <param name="IsSymbolicLink">Whether the entry is a symbolic link.</param>
    /// <param name="Length">Size in bytes. Meaningless for directories, which report it as 0.</param>
    /// <param name="LastWriteTime">Last modification time, as reported by the server.</param>
    /// <param name="Permissions">A <c>drwxr-xr-x</c>-style rendering of the mode bits.</param>
    public sealed record SftpEntry(
        string Name,
        string FullName,
        bool IsDirectory,
        bool IsSymbolicLink,
        long Length,
        DateTime LastWriteTime,
        string Permissions)
    {
        /// <summary>
        /// Whether this is a dot-file. Unix convention, which is what SFTP servers overwhelmingly
        /// are; there is no attribute on the protocol that says "hidden".
        /// </summary>
        public bool IsHidden => Name.Length > 0 && Name[0] == '.' && !IsCurrentOrParentDirectory;

        /// <summary>
        /// The <c>.</c> and <c>..</c> pseudo-entries most servers include in a listing. The panel
        /// navigates with its own controls, so these are filtered rather than shown.
        /// </summary>
        public bool IsCurrentOrParentDirectory => Name is "." or "..";
    }
}
