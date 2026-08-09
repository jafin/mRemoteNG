using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>Opens a local file in whatever the system uses to edit it.</summary>
    public interface IExternalEditor
    {
        void Open(string localPath);
    }

    /// <summary>Asks whether a changed local copy should be written back.</summary>
    public interface IEditPrompts
    {
        bool ConfirmUpload(string fileName);
    }

    /// <summary>
    /// Tracks remote files opened for editing and offers to write back the ones that changed.
    /// </summary>
    /// <remarks>
    /// Sessions are checked on demand rather than continuously — when the file manager is
    /// reactivated and when it closes. That is when the user has plausibly finished editing, and it
    /// avoids a background poll whose only purpose is to interrupt them mid-edit.
    /// </remarks>
    public sealed class RemoteFileEditor : IDisposable
    {
        private readonly IFileSystemBrowser _remote;
        private readonly IExternalEditor _editor;
        private readonly IEditPrompts _prompts;
        private readonly string _rootDirectory;
        private readonly List<RemoteFileEditSession> _sessions = [];
        private readonly Lock _gate = new();

        private bool _disposed;

        public RemoteFileEditor(IFileSystemBrowser remote,
                                IExternalEditor editor,
                                IEditPrompts prompts,
                                string? rootDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(remote);
            ArgumentNullException.ThrowIfNull(editor);
            ArgumentNullException.ThrowIfNull(prompts);

            _remote = remote;
            _editor = editor;
            _prompts = prompts;
            _rootDirectory = rootDirectory ?? Path.Combine(Path.GetTempPath(), "mRemoteNG-edit");
        }

        /// <summary>Sessions currently open. Exposed for tests and for close-time checking.</summary>
        public IReadOnlyList<RemoteFileEditSession> Sessions
        {
            get
            {
                lock (_gate)
                    return _sessions.ToArray();
            }
        }

        /// <summary>Downloads <paramref name="entry"/> and hands it to the editor.</summary>
        public async Task<RemoteFileEditSession> OpenAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ObjectDisposedException.ThrowIf(_disposed, this);

            // A directory per session keeps the real filename without two edits colliding.
            string workingDirectory = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
            RemoteFileEditSession session = new(entry, _remote, workingDirectory);

            try
            {
                await session.DownloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                session.Dispose();
                throw;
            }

            lock (_gate)
                _sessions.Add(session);

            _editor.Open(session.LocalPath);
            return session;
        }

        /// <summary>
        /// Offers to write back every session whose local copy has changed.
        /// </summary>
        /// <returns>How many were uploaded.</returns>
        public async Task<int> WriteBackChangedAsync(CancellationToken cancellationToken = default)
        {
            int uploaded = 0;

            foreach (RemoteFileEditSession session in Sessions)
            {
                if (!session.HasLocalChanges)
                    continue;

                if (!_prompts.ConfirmUpload(session.Entry.Name))
                    continue;

                await session.UploadAsync(cancellationToken).ConfigureAwait(false);
                uploaded++;
            }

            return uploaded;
        }

        /// <summary>Ends one session and removes its local copy.</summary>
        public void Close(RemoteFileEditSession session)
        {
            ArgumentNullException.ThrowIfNull(session);

            lock (_gate)
                _sessions.Remove(session);

            session.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (RemoteFileEditSession session in Sessions)
                session.Dispose();

            lock (_gate)
                _sessions.Clear();

            try
            {
                if (Directory.Exists(_rootDirectory) &&
                    Directory.GetFileSystemEntries(_rootDirectory).Length == 0)
                {
                    Directory.Delete(_rootDirectory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
