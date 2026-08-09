using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.FileTransfer;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Themes;
using mRemoteNG.UI.Controls.FileTransfer;
using WeifenLuo.WinFormsUI.Docking;

namespace mRemoteNG.UI.Window
{
    /// <summary>
    /// A dual-pane file manager for one connection: local and remote side by side, with a transfer
    /// queue beneath.
    /// </summary>
    /// <remarks>
    /// Its own tab rather than a panel beside a session. Two listings and a queue carrying source,
    /// destination, size and progress need the full width; squeezed beside a live terminal none of
    /// it is readable.
    ///
    /// It opens its own SSH connection and does not touch any session tab for the same connection —
    /// sharing a transport is not available. See <c>add-sftp-browser-panel</c> design.md D1.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class FileManagerTab : DockContent
    {
        private readonly ConnectionInfo _connectionInfo;
        private readonly ISftpSession _session;
        private readonly TransferQueue _queue;
        private readonly FilePaneControl _local;
        private readonly FilePaneControl _remote;
        private readonly TransferQueueControl _queueView;
        private readonly SplitContainer _panes = new();
        private readonly SplitContainer _outer = new();

        private bool _connected;

        public FileManagerTab(ConnectionInfo connectionInfo, ISftpSession session)
        {
            ArgumentNullException.ThrowIfNull(connectionInfo);
            ArgumentNullException.ThrowIfNull(session);

            _connectionInfo = connectionInfo;
            _session = session;

            Text = TabText = $"{connectionInfo.Name} (files)";
            DockAreas = DockAreas.Document | DockAreas.Float;

            _queue = new TransferQueue(RunTransferAsync);

            _local = new FilePaneControl(
                new FilePaneController(new LocalFileSystemBrowser(), caseSensitivePaths: false),
                Language.LocalSite, Language.Upload, new FilePanePrompts(this));

            _remote = new FilePaneControl(
                new FilePaneController(new RemoteFileSystemBrowser(session), caseSensitivePaths: true),
                Language.RemoteSite, Language.Download, new FilePanePrompts(this));

            _queueView = new TransferQueueControl(_queue);

            BuildLayout();

            _local.Failed += OnPaneFailed;
            _remote.Failed += OnPaneFailed;
            _local.TransferRequested += (_, entries) => QueueTransfer(entries, TransferDirection.Upload);
            _remote.TransferRequested += (_, entries) => QueueTransfer(entries, TransferDirection.Download);
            _remote.ExternalFilesDropped += OnFilesDroppedOnRemote;
            _local.ExternalFilesDropped += OnFilesDroppedOnLocal;
            _session.Dropped += OnSessionDropped;

            ApplyTheme();
            ThemeManager.getInstance().ThemeChanged += ApplyTheme;

            Load += OnLoad;
        }

        public ConnectionInfo ConnectionInfo => _connectionInfo;

        private void ApplyTheme()
        {
            ThemeManager themeManager = ThemeManager.getInstance();

            _local.ApplyTheme(themeManager);
            _remote.ApplyTheme(themeManager);
            _queueView.ApplyTheme(themeManager);

            if (themeManager.ActiveAndExtended && themeManager.ActiveTheme.ExtendedPalette is { } palette)
            {
                BackColor = palette.getColor("Dialog_Background");
                ForeColor = palette.getColor("Dialog_Foreground");
            }
        }

        private void BuildLayout()
        {
            _panes.Dock = DockStyle.Fill;
            _panes.Orientation = Orientation.Vertical;
            _panes.Panel1.Controls.Add(_local);
            _panes.Panel2.Controls.Add(_remote);
            _local.Dock = DockStyle.Fill;
            _remote.Dock = DockStyle.Fill;

            _outer.Dock = DockStyle.Fill;
            _outer.Orientation = Orientation.Horizontal;
            _outer.Panel1.Controls.Add(_panes);
            _outer.Panel2.Controls.Add(_queueView);
            _queueView.Dock = DockStyle.Fill;

            Controls.Add(_outer);
        }

        private async void OnLoad(object? sender, EventArgs e)
        {
            // Split positions are set after the handle exists: SplitterDistance throws when the
            // container has not been laid out and the value falls outside the panels' minimums.
            TrySetSplitterDistance(_panes, _panes.Width / 2);
            TrySetSplitterDistance(_outer, (int)(_outer.Height * 0.65));

            await _local.StartAsync();
            await ConnectRemoteAsync();
        }

        private async Task ConnectRemoteAsync()
        {
            try
            {
                await _session.ConnectAsync();
                _connected = true;

                foreach (SshCredentialDiagnostic diagnostic in DiagnosticsOf(_session))
                {
                    Runtime.MessageCollector?.AddMessage(
                        diagnostic.Severity == SshCredentialDiagnosticSeverity.Information
                            ? MessageClass.InformationMsg
                            : MessageClass.WarningMsg,
                        diagnostic.Message);
                }

                await _remote.StartAsync();
            }
            catch (Exception ex)
            {
                _connected = false;
                Report($"Could not connect to {_connectionInfo.Hostname}: {ex.Message}", MessageClass.ErrorMsg);
                UpdateTitle();
            }
        }

        private static IReadOnlyList<SshCredentialDiagnostic> DiagnosticsOf(ISftpSession session) =>
            session is SftpSession concrete ? concrete.Diagnostics : [];

        private void OnSessionDropped(object? sender, string reason)
        {
            _connected = false;

            // The listing on screen is no longer the state of the remote host, and must stop being
            // presented as though it is.
            Report($"The file manager's connection to {_connectionInfo.Hostname} dropped. {reason}".Trim(),
                   MessageClass.WarningMsg);

            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(UpdateTitle);
        }

        private void UpdateTitle() =>
            Text = TabText = _connected
                ? $"{_connectionInfo.Name} (files)"
                : $"{_connectionInfo.Name} (files — disconnected)";

        private void QueueTransfer(IReadOnlyList<FileSystemEntry> entries, TransferDirection direction)
        {
            ArgumentNullException.ThrowIfNull(entries);

            FilePaneControl destination = direction == TransferDirection.Upload ? _remote : _local;
            List<TransferItem> items = [];

            foreach (FileSystemEntry entry in entries)
            {
                // Directories would need recursive enumeration, which is out of scope; queueing them
                // silently would produce a queue full of items that cannot succeed.
                if (entry.IsDirectory)
                {
                    Report($"Skipped {entry.Name}: transferring a whole directory is not supported yet.",
                           MessageClass.InformationMsg);
                    continue;
                }

                string target = destination.Controller.Combine(destination.Controller.CurrentPath, entry.Name);
                items.Add(new TransferItem(direction, entry.FullPath, target, entry.Length));
            }

            _queue.EnqueueRange(items);
        }

        /// <summary>
        /// Files dragged in from Explorer are queued for upload to the remote pane's directory.
        /// </summary>
        private void OnFilesDroppedOnRemote(object? sender, IReadOnlyList<string> paths) =>
            QueueTransfer(DescribeLocalPaths(paths), TransferDirection.Upload);

        /// <summary>
        /// A drop onto the local pane is a local file copy, which Explorer already does better.
        /// Reported rather than silently ignored, so the gesture does not just appear to fail.
        /// </summary>
        private void OnFilesDroppedOnLocal(object? sender, IReadOnlyList<string> paths) =>
            Report("Dropped files are only uploaded when dropped on the remote pane.",
                   MessageClass.InformationMsg);

        private static IReadOnlyList<FileSystemEntry> DescribeLocalPaths(IReadOnlyList<string> paths)
        {
            List<FileSystemEntry> entries = [];

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    // Marked as a directory so QueueTransfer reports it as unsupported, rather than
                    // queueing an item that could only fail.
                    entries.Add(new FileSystemEntry(Path.GetFileName(path), path, true, 0,
                                                    DateTime.Now, string.Empty, false));
                    continue;
                }

                if (!File.Exists(path))
                    continue;

                FileInfo info = new(path);
                entries.Add(new FileSystemEntry(info.Name, info.FullName, false, info.Length,
                                                info.LastWriteTime, string.Empty, false));
            }

            return entries;
        }

        /// <summary>
        /// Moves one queued file.
        /// </summary>
        /// <remarks>
        /// Streams straight from one side to the other rather than through a temporary file: the
        /// source's read stream is handed to the destination's write.
        /// </remarks>
        private async Task RunTransferAsync(TransferItem item, IProgress<long> progress, CancellationToken cancellationToken)
        {
            IFileSystemBrowser source = item.Direction == TransferDirection.Upload
                ? _local.Controller.Browser
                : _remote.Controller.Browser;

            IFileSystemBrowser destination = item.Direction == TransferDirection.Upload
                ? _remote.Controller.Browser
                : _local.Controller.Browser;

            Stream stream = await source.OpenReadAsync(item.SourcePath, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                await destination.WriteAsync(stream, item.DestinationPath, item.Size, progress, cancellationToken)
                                 .ConfigureAwait(false);
            }

            await RefreshAsync(item.Direction == TransferDirection.Upload ? _remote : _local).ConfigureAwait(false);
        }

        private Task RefreshAsync(FilePaneControl pane)
        {
            if (!IsHandleCreated || IsDisposed)
                return Task.CompletedTask;

            TaskCompletionSource completion = new();

            // Not an async lambda: an async void delegate swallows its exceptions rather than
            // letting the returned task carry them.
            BeginInvoke(() => _ = RefreshCoreAsync(pane, completion));

            return completion.Task;
        }

        private static async Task RefreshCoreAsync(FilePaneControl pane, TaskCompletionSource completion)
        {
            try
            {
                await pane.Controller.RefreshAsync();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private void OnPaneFailed(object? sender, string message)
        {
            string side = sender is FilePaneControl pane ? pane.Caption : "File manager";
            Report($"{side}: {message}", MessageClass.WarningMsg);
        }

        private static void Report(string message, MessageClass messageClass) =>
            Runtime.MessageCollector?.AddMessage(messageClass, message);

        private static void TrySetSplitterDistance(SplitContainer container, int distance)
        {
            int available = container.Orientation == Orientation.Vertical
                ? container.Width - container.SplitterWidth
                : container.Height - container.SplitterWidth;

            if (available <= 0)
                return;

            int clamped = Math.Clamp(distance, container.Panel1MinSize, available - container.Panel2MinSize);
            if (clamped >= container.Panel1MinSize && clamped <= available - container.Panel2MinSize)
                container.SplitterDistance = clamped;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.getInstance().ThemeChanged -= ApplyTheme;
                _session.Dropped -= OnSessionDropped;
                _queue.Dispose();
                _session.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
