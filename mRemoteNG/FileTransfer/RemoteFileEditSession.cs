using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// One remote file opened for local editing.
/// </summary>
/// <remarks>
/// <para>
/// Downloads the file to a temporary location, notices when the local copy changes, and can put
/// it back. Owns no UI and no editor process, so the decisions worth testing — has it changed,
/// does it get uploaded, is the copy cleaned up — are testable directly.
/// </para>
/// <para>
/// <b>The file is unencrypted on local disk for the life of the session.</b> It is placed in a
/// per-session directory and deleted on <see cref="Dispose"/>. That cleanup does not run if the
/// process is killed, exactly as with the temporary private key files that
/// <c>add-ssh-agent-key-injection</c> exists to remove. Recorded here rather than implied to be
/// handled.
/// </para>
/// </remarks>
public sealed class RemoteFileEditSession : IDisposable
{
    private readonly IFileSystemBrowser _remote;
    private readonly string _workingDirectory;

    private DateTime _downloadedWriteTimeUtc;
    private long _downloadedLength = -1;
    private bool _disposed;

    public RemoteFileEditSession(FileSystemEntry entry, IFileSystemBrowser remote, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        Entry = entry;
        _remote = remote;
        _workingDirectory = workingDirectory;

        // Its own directory, so a file keeps its real name — an editor's syntax highlighting and
        // an interpreter's shebang both key off the extension, and a mangled temp name breaks
        // both. It also stops two edits of same-named files in different remote directories
        // colliding.
        LocalPath = Path.Combine(workingDirectory, entry.Name);
    }

    public FileSystemEntry Entry { get; }

    /// <summary>Where the local copy lives.</summary>
    public string LocalPath { get; }

    /// <summary>Whether the local copy has been downloaded yet.</summary>
    public bool IsDownloaded => _downloadedLength >= 0;

    /// <summary>
    /// Whether the local copy differs from what was downloaded.
    /// </summary>
    /// <remarks>
    /// Compares last-write time and length against a snapshot taken immediately after the
    /// download, rather than watching the file. A watcher would fire on the editor's own
    /// intermediate writes and on save-to-temp-then-rename patterns, and would have to be
    /// debounced; comparing state answers the only question that matters — is what is on disk
    /// now different from what was put there — without any timing to get wrong.
    /// </remarks>
    public bool HasLocalChanges
    {
        get
        {
            if (!IsDownloaded)
                return false;

            FileInfo info = new(LocalPath);
            if (!info.Exists)
                return false;

            return info.LastWriteTimeUtc != _downloadedWriteTimeUtc || info.Length != _downloadedLength;
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Directory.CreateDirectory(_workingDirectory);

        Stream source = await _remote.OpenReadAsync(Entry.FullPath, cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            FileStream destination = new(LocalPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (destination.ConfigureAwait(false))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        Snapshot();
    }

    /// <summary>
    /// Writes the local copy back over the remote file and re-snapshots.
    /// </summary>
    public async Task UploadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsDownloaded)
            throw new InvalidOperationException("The file has not been downloaded yet.");

        FileInfo info = new(LocalPath);

        FileStream source = new(LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using (source.ConfigureAwait(false))
        {
            await _remote.WriteAsync(source, Entry.FullPath, info.Length, null, cancellationToken)
                .ConfigureAwait(false);
        }

        // Re-snapshot so the same edit is not offered again next time the session is checked.
        Snapshot();
    }

    private void Snapshot()
    {
        FileInfo info = new(LocalPath);
        _downloadedWriteTimeUtc = info.LastWriteTimeUtc;
        _downloadedLength = info.Exists ? info.Length : 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (File.Exists(LocalPath))
                File.Delete(LocalPath);

            if (Directory.Exists(_workingDirectory) &&
                Directory.GetFileSystemEntries(_workingDirectory).Length == 0)
            {
                Directory.Delete(_workingDirectory);
            }
        }
        catch (IOException)
        {
            // The editor still has it open. Left behind rather than failing the close; it is in
            // the user's temp directory either way.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}