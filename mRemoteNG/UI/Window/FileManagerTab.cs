using System;
using System.Collections.Generic;
using System.Globalization;
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
    public sealed class FileManagerTab : DockContent, IPaneConnection
    {
        private readonly ConnectionInfo _connectionInfo;
        private readonly ISftpSession _session;
        private readonly TransferQueue _queue;
        private readonly FilePaneControl _local;
        private readonly FilePaneControl _remote;
        private readonly TransferQueueControl _queueView;
        private readonly SplitContainer _panes = new();
        private readonly SplitContainer _outer = new();

        private readonly RemoteFileEditor _editor;

        /// <summary>
        /// Cancels directory expansions still in progress. Replaced after each cancellation, because a
        /// cancelled source cannot be reused and the next transfer must still be stoppable.
        /// </summary>
        private CancellationTokenSource _expansion = new();

        private readonly ContextMenuStrip _tabMenu = new();
        private readonly ToolStripMenuItem _tabReconnect;

        /// <summary>
        /// Guards against two reconnects at once. The pane button, the tab menu and a refresh are three
        /// routes to the same operation, and an impatient user will take more than one; two attempts in
        /// flight means two authentications and two clients, one of which is then abandoned.
        /// </summary>
        private readonly SingleFlight _reconnect = new();

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

            LocalFileSystemBrowser localBrowser = new();
            RemoteFileSystemBrowser remoteBrowser = new(session);

            _local = new FilePaneControl(
                new FilePaneController(localBrowser, localBrowser.PathsAreCaseSensitive),
                Language.LocalSite, Language.Upload, new FilePanePrompts(this));

            // The local pane is given no connection: there is nothing to reconnect a local filesystem
            // to, and the absent dependency is what keeps the reconnect controls off that side.
            _remote = new FilePaneControl(
                new FilePaneController(remoteBrowser, remoteBrowser.PathsAreCaseSensitive),
                Language.RemoteSite, Language.Download, new FilePanePrompts(this), this);

            _tabReconnect = new ToolStripMenuItem(Language.Reconnect, null, (_, _) => _ = ReconnectFromMenuAsync());
            _tabMenu.Items.Add(_tabReconnect);
            _tabMenu.Opening += (_, _) => _tabReconnect.Enabled = !IsConnected;
            TabPageContextMenuStrip = _tabMenu;

            _queueView = new TransferQueueControl(_queue);

            _editor = new RemoteFileEditor(_remote.Controller.Browser,
                                           new ShellExternalEditor(),
                                           new EditPrompts(this));

            BuildLayout();

            _local.Failed += OnPaneFailed;
            _remote.Failed += OnPaneFailed;
            _local.TransferRequested += (_, entries) => QueueTransfer(entries, TransferDirection.Upload);
            _remote.TransferRequested += (_, entries) => QueueTransfer(entries, TransferDirection.Download);
            _remote.ExternalFilesDropped += OnFilesDroppedOnRemote;
            _local.ExternalFilesDropped += OnFilesDroppedOnLocal;
            _local.DeleteRequested += (_, entries) => QueueDeletion(_local, entries, TransferDirection.Upload);
            _remote.DeleteRequested += (_, entries) => QueueDeletion(_remote, entries, TransferDirection.Download);
            _remote.FileActivated += OnRemoteFileActivated;
            _queue.AllCancelled += OnQueueCancelled;

            // Deleting a thousand files would otherwise re-list a thousand times. Both panes, because a
            // drained queue may have held work for either.
            _queue.Drained += OnQueueDrained;
            Activated += OnTabActivated;
            FormClosing += OnTabClosing;
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

                ReportDiagnostics();

                await _remote.StartAsync();
            }
            catch (Exception ex)
            {
                _connected = false;
                Report($"Could not connect to {_connectionInfo.Hostname}: {ex.Message}", MessageClass.ErrorMsg);
                UpdateTitle();
            }
            finally
            {
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static IReadOnlyList<SshCredentialDiagnostic> DiagnosticsOf(ISftpSession session) =>
            session is SftpSession concrete ? concrete.Diagnostics : [];

        /// <inheritdoc />
        public bool IsConnected => _connected && _session.IsConnected;

        /// <inheritdoc />
        public event EventHandler? ConnectionChanged;

        /// <summary>
        /// Re-establishes the dropped session, at most one attempt at a time.
        /// </summary>
        /// <remarks>
        /// A second caller is turned away rather than queued. Queueing would mean the second attempt
        /// runs after the first has already succeeded, reconnecting a healthy session for no reason.
        /// </remarks>
        public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected)
                return true;

            if (!_reconnect.TryEnter())
                return false;

            try
            {
                await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                _connected = true;

                ReportDiagnostics();
                Report($"Reconnected to {_connectionInfo.Hostname}.", MessageClass.InformationMsg);
                return true;
            }
            catch (Exception ex)
            {
                _connected = false;
                Report($"Could not reconnect to {_connectionInfo.Hostname}: {ex.Message}", MessageClass.WarningMsg);
                return false;
            }
            finally
            {
                _reconnect.Exit();

                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(UpdateTitle);

                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Reconnects from the tab's own menu, and lists once it works.
        /// </summary>
        /// <remarks>
        /// The same guarded operation the pane's button calls, so pressing both changes nothing. Two
        /// entry points because the two are noticed at different moments: the tab title is what says
        /// the session dropped, so somebody who has been working elsewhere reads the problem there.
        /// </remarks>
        private async Task ReconnectFromMenuAsync()
        {
            try
            {
                if (await ReconnectAsync().ConfigureAwait(true))
                    await _remote.Controller.RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Report($"Could not reconnect to {_connectionInfo.Hostname}: {ex.Message}", MessageClass.WarningMsg);
            }
        }

        private void ReportDiagnostics()
        {
            foreach (SshCredentialDiagnostic diagnostic in DiagnosticsOf(_session))
            {
                Runtime.MessageCollector?.AddMessage(
                    diagnostic.Severity == SshCredentialDiagnosticSeverity.Information
                        ? MessageClass.InformationMsg
                        : MessageClass.WarningMsg,
                    diagnostic.Message);
            }
        }

        private void OnSessionDropped(object? sender, string reason)
        {
            _connected = false;

            // The listing on screen is no longer the state of the remote host, and must stop being
            // presented as though it is.
            Report($"The file manager's connection to {_connectionInfo.Hostname} dropped. {reason}".Trim(),
                   MessageClass.WarningMsg);

            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(UpdateTitle);

            // Lights up the reconnect controls. Deliberately no prompt: a flaky link drops repeatedly,
            // and a modal per drop would interrupt whatever the user moved on to in order to ask a
            // question the button already answers whenever they choose to look.
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateTitle() =>
            Text = TabText = _connected
                ? $"{_connectionInfo.Name} (files)"
                : $"{_connectionInfo.Name} (files — disconnected)";

        /// <summary>
        /// Expands the selection and queues everything in it.
        /// </summary>
        /// <remarks>
        /// Files and directories take the same path deliberately. A directory needs walking and a file
        /// does not, but both need the destination checked for something already there, and having one
        /// route through means a folder and a multi-file selection cannot disagree about what happens
        /// when they collide.
        /// </remarks>
        private void QueueTransfer(IReadOnlyList<FileSystemEntry> entries, TransferDirection direction)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
                return;

            FilePaneControl sourcePane = direction == TransferDirection.Upload ? _local : _remote;
            FilePaneControl destinationPane = direction == TransferDirection.Upload ? _remote : _local;

            // One expander for the whole gesture: the overwrite question is asked once even when the
            // user selected several folders at once, and a later transfer gets a fresh one and asks again.
            DirectoryTransferExpander expander = new(sourcePane.Controller.Browser,
                                                     destinationPane.Controller.Browser,
                                                     new TransferConflictPrompt(this));

            expander.Skipped += OnExpansionSkipped;

            string destinationPath = destinationPane.Controller.CurrentPath;

            _ = ExpandAndQueueAsync(expander, [.. entries], direction, destinationPath, _expansion.Token);
        }

        /// <summary>
        /// Walks the selection in the background, queueing each file as it is found.
        /// </summary>
        /// <remarks>
        /// Enqueued as they arrive rather than collected first. Walking a large tree over a slow link is
        /// minutes of round trips, and a queue that stayed empty throughout would look broken while the
        /// transfer was in fact under way — the first file starts moving while the rest is still being
        /// discovered.
        /// </remarks>
        private async Task ExpandAndQueueAsync(DirectoryTransferExpander expander,
                                               IReadOnlyList<FileSystemEntry> entries,
                                               TransferDirection direction,
                                               string destinationPath,
                                               CancellationToken cancellationToken)
        {
            try
            {
                foreach (FileSystemEntry entry in entries)
                {
                    await foreach (TransferPlanItem item in
                                   expander.ExpandAsync(entry, destinationPath, cancellationToken)
                                           .ConfigureAwait(false))
                    {
                        _queue.Enqueue(new TransferItem(direction, item.SourcePath, item.DestinationPath, item.Length));
                    }

                    if (expander.WasCancelledByUser)
                        break;
                }

                if (expander.SkippedExistingCount > 0)
                    Report(string.Format(CultureInfo.CurrentCulture,
                                         Language.TransferSkippedExisting,
                                         expander.SkippedExistingCount),
                           MessageClass.InformationMsg);
            }
            catch (OperationCanceledException)
            {
                // The queue was cancelled or the tab closed. Both are the user's doing and neither is
                // worth a message.
            }
            catch (Exception ex)
            {
                // A failure of the walk itself, as opposed to one branch of it, which the expander
                // reports and recovers from on its own.
                Report($"Could not expand the selection: {ex.Message}", MessageClass.WarningMsg);
            }
            finally
            {
                expander.Skipped -= OnExpansionSkipped;
            }
        }

        /// <summary>
        /// Queues a confirmed deletion, deepest entries first.
        /// </summary>
        /// <remarks>
        /// Through the queue rather than a loop here, which is what makes deleting a tree acceptable:
        /// every entry is a row the user can see, the work runs one at a time, and "Cancel all" stops
        /// the rest. The confirmation has already been given by the pane's command.
        /// </remarks>
        private void QueueDeletion(FilePaneControl pane,
                                   IReadOnlyList<FileSystemEntry> entries,
                                   TransferDirection side)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
                return;

            DirectoryDeletionPlanner planner = new(pane.Controller.Browser);
            planner.Skipped += OnExpansionSkipped;

            _ = PlanAndQueueDeletionAsync(planner, pane, [.. entries], side, _expansion.Token);
        }

        private async Task PlanAndQueueDeletionAsync(DirectoryDeletionPlanner planner,
                                                     FilePaneControl pane,
                                                     IReadOnlyList<FileSystemEntry> entries,
                                                     TransferDirection side,
                                                     CancellationToken cancellationToken)
        {
            try
            {
                foreach (FileSystemEntry entry in entries)
                {
                    await foreach (FileSystemEntry doomed in
                                   planner.PlanAsync(entry, cancellationToken).ConfigureAwait(false))
                    {
                        _queue.Enqueue(TransferItem.Deletion(side, doomed));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The queue was cancelled or the tab closed. Both are the user's doing.
            }
            catch (Exception ex)
            {
                Report($"Could not plan the deletion: {ex.Message}", MessageClass.WarningMsg);
            }
            finally
            {
                planner.Skipped -= OnExpansionSkipped;
                await RefreshAsync(pane).ConfigureAwait(false);
            }
        }

        private void OnExpansionSkipped(object? sender, string message) =>
            Report(message, MessageClass.InformationMsg);

        /// <summary>
        /// Stops any walk still running when the whole queue is cancelled.
        /// </summary>
        /// <remarks>
        /// Without this, "Cancel all" would empty the queue and then watch an expansion still in
        /// progress fill it straight back up.
        /// </remarks>
        private void OnQueueDrained(object? sender, EventArgs e)
        {
            _ = RefreshAsync(_local);
            _ = RefreshAsync(_remote);
        }

        private void OnQueueCancelled(object? sender, EventArgs e)
        {
            CancellationTokenSource previous = Interlocked.Exchange(ref _expansion, new CancellationTokenSource());

            previous.Cancel();
            previous.Dispose();
        }

        /// <summary>
        /// Double-clicking a remote file downloads it and opens the local editor.
        /// </summary>
        private void OnRemoteFileActivated(object? sender, FileSystemEntry entry) =>
            _ = OpenForEditingAsync(entry);

        private async Task OpenForEditingAsync(FileSystemEntry entry)
        {
            try
            {
                await _editor.OpenAsync(entry);
            }
            catch (Exception ex)
            {
                Report($"Could not open {entry.Name} for editing: {ex.Message}", MessageClass.WarningMsg);
            }
        }

        /// <summary>
        /// Checks for edited files when the user comes back to the tab.
        /// </summary>
        /// <remarks>
        /// Rather than polling: this is when they have plausibly finished editing, and a background
        /// poll's only achievement would be interrupting them mid-edit.
        /// </remarks>
        private void OnTabActivated(object? sender, EventArgs e) => _ = WriteBackChangedAsync();

        private void OnTabClosing(object? sender, FormClosingEventArgs e) => _ = WriteBackChangedAsync();

        private async Task WriteBackChangedAsync()
        {
            try
            {
                await _editor.WriteBackChangedAsync();
            }
            catch (Exception ex)
            {
                Report($"Could not write back an edited file: {ex.Message}", MessageClass.WarningMsg);
            }
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
                    DirectoryInfo directory = new(path);
                    entries.Add(new FileSystemEntry(directory.Name, directory.FullName, true, 0,
                                                    directory.LastWriteTime, string.Empty, false,
                                                    directory.Attributes.HasFlag(FileAttributes.ReparsePoint)));
                    continue;
                }

                if (!File.Exists(path))
                    continue;

                FileInfo info = new(path);
                entries.Add(new FileSystemEntry(info.Name, info.FullName, false, info.Length,
                                                info.LastWriteTime, string.Empty, false,
                                                info.Attributes.HasFlag(FileAttributes.ReparsePoint)));
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
            if (item.Kind == TransferOperationKind.Delete)
            {
                await RunDeletionAsync(item, cancellationToken).ConfigureAwait(false);
                return;
            }

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

        /// <summary>
        /// Removes one queued entry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Calls the browser's single-entry delete, which still refuses a non-empty directory. That
        /// refusal is the backstop the planner's ordering is checked against: if children were somehow
        /// queued after their parent, the parent fails loudly here rather than taking the tree with it.
        /// </para>
        /// <para>
        /// The pane is not refreshed per entry — deleting a thousand files would otherwise re-list a
        /// thousand times. It is refreshed when the queue drains.
        /// </para>
        /// </remarks>
        private async Task RunDeletionAsync(TransferItem item, CancellationToken cancellationToken)
        {
            if (item.DeleteTarget is not { } entry)
                throw new InvalidOperationException("A deletion item carries no entry to delete.");

            FilePaneControl pane = item.Direction == TransferDirection.Upload ? _local : _remote;

            await pane.Controller.Browser.DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
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
                _queue.AllCancelled -= OnQueueCancelled;
                _queue.Drained -= OnQueueDrained;

                // Before the queue and session go: a walk still running would otherwise keep calling
                // into a disposed session.
                _expansion.Cancel();
                _expansion.Dispose();

                _tabMenu.Dispose();
                _editor.Dispose();
                _queue.Dispose();
                _session.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
