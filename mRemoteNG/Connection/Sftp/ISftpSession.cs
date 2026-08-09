using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.Connection.Sftp
{
    /// <summary>
    /// Progress of a running transfer.
    /// </summary>
    /// <param name="Transferred">Bytes moved so far.</param>
    /// <param name="Total">Total bytes, or -1 when the size is not known.</param>
    public readonly record struct SftpTransferProgress(long Transferred, long Total)
    {
        /// <summary>Completed fraction, or <see langword="null"/> when the total is unknown.</summary>
        public double? Fraction => Total > 0 ? (double)Transferred / Total : null;
    }

    /// <summary>
    /// Thrown when an operation is attempted on a session that is not connected.
    /// </summary>
    /// <remarks>
    /// A distinct type because the panel must tell "the session dropped" apart from "the server
    /// refused this operation". The first means the listing on screen is stale and must stop being
    /// presented as current; the second means the listing is fine and one action failed.
    /// </remarks>
    public class SftpSessionNotConnectedException : InvalidOperationException
    {
        public SftpSessionNotConnectedException()
            : base("The SFTP session is not connected.")
        {
        }

        public SftpSessionNotConnectedException(string message)
            : base(message)
        {
        }

        public SftpSessionNotConnectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// A connection to a remote filesystem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behind an interface so the panel can be built and tested without a server. No test in this
    /// repository may require one.
    /// </para>
    /// <para>
    /// Every operation is asynchronous and cancellable. A listing over a slow link takes seconds,
    /// and running that on the UI thread would freeze the whole application — including the session
    /// beside the panel.
    /// </para>
    /// </remarks>
    public interface ISftpSession : IDisposable
    {
        /// <summary>Whether the session is currently usable.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Raised when the session drops for any reason other than an explicit disconnect. The
        /// panel uses this to stop presenting its listing as current.
        /// </summary>
        event EventHandler<string>? Dropped;

        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>The directory the server places the user in, used as the panel's home.</summary>
        string HomeDirectory { get; }

        /// <summary>
        /// Lists <paramref name="path"/>, excluding the <c>.</c> and <c>..</c> pseudo-entries.
        /// </summary>
        Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Writes a remote file into <paramref name="destination"/>.</summary>
        Task DownloadAsync(SftpEntry file,
                           Stream destination,
                           IProgress<SftpTransferProgress>? progress = null,
                           CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes <paramref name="source"/> to <paramref name="remotePath"/>.
        /// </summary>
        /// <param name="totalBytes">
        /// The expected size, for progress. Supplied by the caller because a non-seekable source
        /// cannot report one.
        /// </param>
        Task UploadAsync(Stream source,
                         string remotePath,
                         long? totalBytes = null,
                         IProgress<SftpTransferProgress>? progress = null,
                         CancellationToken cancellationToken = default);

        Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default);

        Task DeleteAsync(SftpEntry entry, CancellationToken cancellationToken = default);

        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        Task CreateFileAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Whether anything exists at <paramref name="path"/>, file or directory.</summary>
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Follows <paramref name="path"/> and reports whether it lands on a directory.
        /// </summary>
        /// <remarks>
        /// Deliberately a <i>following</i> stat, unlike a listing. A listing reports a symbolic link as
        /// a link, so it cannot say what the link points at; this can, which is what lets a recursive
        /// transfer treat a link to a file as a file and refuse to descend into a link to a directory.
        /// </remarks>
        Task<bool> ResolvesToDirectoryAsync(string path, CancellationToken cancellationToken = default);
    }
}
