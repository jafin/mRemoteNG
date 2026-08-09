using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;
using mRemoteNG.Themes;

namespace mRemoteNG.UI.Controls.FileTransfer
{
    /// <summary>
    /// One side of the file manager: a path box, a navigation toolbar and a listing.
    /// </summary>
    /// <remarks>
    /// A thin view over <see cref="FilePaneController"/>, which owns every rule worth testing. This
    /// class does layout, marshalling and event plumbing and nothing else.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class FilePaneControl : UserControl
    {
        private readonly FilePaneController _controller;
        private readonly ObjectListView _list = new();
        private readonly ToolStrip _toolbar = new();
        private readonly ToolStripTextBox _pathBox = new();
        private readonly ToolStripButton _back = new();
        private readonly ToolStripButton _forward = new();
        private readonly ToolStripButton _up = new();
        private readonly ToolStripButton _home = new();
        private readonly ToolStripButton _refresh = new();
        private readonly ToolStripButton? _reconnect;
        private readonly ToolStripButton _hidden = new();
        private readonly IPaneConnection? _connection;
        private readonly ImageList _icons = new();
        private readonly ToolStripButton _transfer = new();
        private readonly ToolStripButton _newFolder = new();
        private readonly ToolStripButton _newFile = new();
        private readonly ToolStripButton _rename = new();
        private readonly ToolStripButton _delete = new();
        private readonly ContextMenuStrip _contextMenu = new();
        private readonly Label _status = new();
        private readonly FilePaneCommands _commands;

        public FilePaneControl(FilePaneController controller, string caption, string transferCaption)
            : this(controller, caption, transferCaption, new FilePanePrompts(null))
        {
        }

        /// <param name="connection">
        /// The connection behind this pane, or <see langword="null"/> when there is nothing to
        /// reconnect. That null is what makes reconnection remote-only without this class ever asking
        /// which side it is.
        /// </param>
        public FilePaneControl(FilePaneController controller,
                               string caption,
                               string transferCaption,
                               IFilePanePrompts prompts,
                               IPaneConnection? connection = null)
        {
            ArgumentNullException.ThrowIfNull(controller);
            ArgumentNullException.ThrowIfNull(prompts);

            _controller = controller;
            Caption = caption;
            TransferCaption = transferCaption;
            _commands = new FilePaneCommands(controller, prompts);
            _connection = connection;

            if (connection is not null)
            {
                _reconnect = new ToolStripButton();
                connection.ConnectionChanged += OnConnectionChanged;
            }

            BuildToolbar();
            BuildList();
            BuildContextMenu();
            BuildStatus();

            Controls.Add(_list);
            Controls.Add(_toolbar);
            Controls.Add(_status);

            _controller.EntriesChanged += OnEntriesChanged;
            _controller.OperationFailed += OnOperationFailed;
            _controller.BusyChanged += OnBusyChanged;
        }

        /// <summary>Which side this is, for labelling.</summary>
        public string Caption { get; }

        /// <summary>What moving the selection to the other pane is called from here.</summary>
        public string TransferCaption { get; }

        /// <summary>Raised when files are dropped from outside the application.</summary>
        public event EventHandler<IReadOnlyList<string>>? ExternalFilesDropped;

        public FilePaneController Controller => _controller;

        /// <summary>Raised when the user asks to transfer the current selection to the other pane.</summary>
        public event EventHandler<IReadOnlyList<FileSystemEntry>>? TransferRequested;

        /// <summary>Raised when the user activates a file (as opposed to opening a directory).</summary>
        public event EventHandler<FileSystemEntry>? FileActivated;

        /// <summary>Raised when an operation fails, so the tab can surface it.</summary>
        public event EventHandler<string>? Failed;

        /// <summary>
        /// The selected entries, never including the <c>..</c> row.
        /// </summary>
        /// <remarks>
        /// Filtered here rather than in each command. Transfer, rename and delete all read this, and
        /// one of the three would eventually be written without the guard — at which point the file
        /// manager would offer to delete the parent directory.
        /// </remarks>
        public IReadOnlyList<FileSystemEntry> SelectedEntries =>
            [.. _list.SelectedObjects.Cast<FileSystemEntry>().Where(entry => !entry.IsParentNavigation)];

        public Task StartAsync() => _controller.NavigateHomeAsync();

        /// <summary>
        /// Applies the active theme, when it supplies an extended palette.
        /// </summary>
        /// <remarks>
        /// Only the list and toolbar are recoloured. A theme that does not carry an extended palette
        /// leaves the control at system colours, which is what the rest of the application does.
        /// </remarks>
        public void ApplyTheme(ThemeManager themeManager)
        {
            ArgumentNullException.ThrowIfNull(themeManager);

            if (!themeManager.ActiveAndExtended || themeManager.ActiveTheme.ExtendedPalette is not { } palette)
                return;

            BackColor = palette.getColor("Dialog_Background");
            ForeColor = palette.getColor("Dialog_Foreground");
            _list.BackColor = palette.getColor("TextBox_Background");
            _list.ForeColor = palette.getColor("TextBox_Foreground");
            _toolbar.BackColor = palette.getColor("Dialog_Background");
            _toolbar.ForeColor = palette.getColor("Dialog_Foreground");
            _status.BackColor = palette.getColor("Dialog_Background");
            _status.ForeColor = palette.getColor("Dialog_Foreground");
        }

        private void BuildToolbar()
        {
            _toolbar.Dock = DockStyle.Top;
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;

            Configure(_back, "◀", "Back", async () => await _controller.GoBackAsync());
            Configure(_forward, "▶", "Forward", async () => await _controller.GoForwardAsync());
            Configure(_up, "▲", "Up", async () => await _controller.NavigateUpAsync());
            Configure(_home, "⌂", "Home", async () => await _controller.NavigateHomeAsync());
            Configure(_refresh, "⟳", Language.Refresh, RefreshAsync);

            if (_reconnect is not null)
                Configure(_reconnect, "⚡", Language.Reconnect, ReconnectAsync);

            _hidden.Text = "•";
            _hidden.ToolTipText = "Show hidden entries";
            _hidden.CheckOnClick = true;
            _hidden.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _hidden.CheckedChanged += (_, _) => _controller.ShowHidden = _hidden.Checked;

            _pathBox.AutoSize = false;
            _pathBox.Width = 320;
            _pathBox.KeyDown += OnPathBoxKeyDown;

            Configure(_transfer, TransferCaption, TransferCaption, () => { RequestTransfer(); return Task.CompletedTask; });
            Configure(_newFolder, Language.NewFolder, Language.NewFolder, async () => await _commands.NewFolderAsync());
            Configure(_newFile, Language.NewFile, Language.NewFile, async () => await _commands.NewFileAsync());
            Configure(_rename, Language.Rename, Language.Rename, RenameSelectionAsync);
            Configure(_delete, Language.Delete, Language.Delete, DeleteSelectionAsync);

            _toolbar.Items.AddRange([_back, _forward, _up, _home, _refresh]);

            if (_reconnect is not null)
                _toolbar.Items.Add(_reconnect);

            _toolbar.Items.AddRange([new ToolStripSeparator(), _hidden,
                                     new ToolStripSeparator(), _transfer,
                                     new ToolStripSeparator(), _newFolder, _newFile, _rename, _delete,
                                     new ToolStripSeparator(), _pathBox]);
        }

        private void BuildContextMenu()
        {
            ToolStripMenuItem transfer = new(TransferCaption, null, (_, _) => RequestTransfer());
            ToolStripMenuItem rename = new(Language.Rename, null, (_, _) => RunCommand(RenameSelectionAsync));
            ToolStripMenuItem delete = new(Language.Delete, null, (_, _) => RunCommand(DeleteSelectionAsync));
            ToolStripMenuItem newFolder = new(Language.NewFolder, null, (_, _) => RunCommand(async () => await _commands.NewFolderAsync()));
            ToolStripMenuItem newFile = new(Language.NewFile, null, (_, _) => RunCommand(async () => await _commands.NewFileAsync()));
            ToolStripMenuItem refresh = new(Language.Refresh, null, (_, _) => RunCommand(_controller.RefreshAsync));

            _contextMenu.Items.AddRange([transfer, new ToolStripSeparator(),
                                         rename, delete, new ToolStripSeparator(),
                                         newFolder, newFile, new ToolStripSeparator(), refresh]);

            _contextMenu.Opening += (_, _) =>
            {
                bool hasSelection = SelectedEntries.Count > 0;
                transfer.Enabled = hasSelection;
                delete.Enabled = hasSelection;
                rename.Enabled = SelectedEntries.Count == 1;
            };

            _list.ContextMenuStrip = _contextMenu;
        }

        /// <summary>
        /// Lists the current directory, reconnecting first if the session has dropped.
        /// </summary>
        /// <remarks>
        /// Refresh is the reflex when a pane looks wrong, so it does the obvious thing. A connected
        /// pane is unaffected — the guard is false and it lists exactly as before. Listing after a
        /// failed reconnect is skipped deliberately: it would fail with the same "not connected" error
        /// the reconnect just reported, giving one gesture two errors.
        /// </remarks>
        private async Task RefreshAsync()
        {
            if (await ShouldListAsync(_connection).ConfigureAwait(true))
                await _controller.RefreshAsync();
        }

        /// <summary>
        /// Whether to go ahead and list, reconnecting first if the session has dropped.
        /// </summary>
        /// <remarks>
        /// Separated out so the rule can be asserted without a control. There are three cases and only
        /// one of them is interesting: no connection to speak of and a healthy connection both list as
        /// before, and a dropped one lists only if it came back. Listing after a failed reconnect is
        /// skipped deliberately — it would fail with the same "not connected" error the reconnect just
        /// reported, giving one gesture two errors.
        /// </remarks>
        internal static async Task<bool> ShouldListAsync(IPaneConnection? connection) =>
            connection is not { IsConnected: false } || await connection.ReconnectAsync().ConfigureAwait(false);

        private async Task ReconnectAsync()
        {
            if (_connection is not null && await _connection.ReconnectAsync().ConfigureAwait(true))
                await _controller.RefreshAsync();
        }

        private void OnConnectionChanged(object? sender, EventArgs e) => RunOnUi(UpdateConnectionState);

        private void UpdateConnectionState()
        {
            if (_reconnect is not null && _connection is not null)
                _reconnect.Enabled = !_connection.IsConnected;
        }

        private async Task RenameSelectionAsync()
        {
            IReadOnlyList<FileSystemEntry> selection = SelectedEntries;
            if (selection.Count == 1)
                await _commands.RenameAsync(selection[0]);
        }

        private async Task DeleteSelectionAsync() => await _commands.DeleteAsync(SelectedEntries);

        private void Configure(ToolStripButton button, string glyph, string tooltip, Func<Task> action)
        {
            button.Text = glyph;
            button.ToolTipText = tooltip;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Click += (_, _) => RunCommand(action);
        }

        /// <summary>
        /// Starts a command from a UI event.
        /// </summary>
        /// <remarks>
        /// Not an async lambda on the handler: an async void delegate drops its exceptions on the
        /// floor, and a command that throws would leave the pane looking like nothing happened.
        /// </remarks>
        private void RunCommand(Func<Task> action) => _ = RunCommandAsync(action);

        private async Task RunCommandAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(this, ex.Message);
            }
        }

        private void BuildList()
        {
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.UseFiltering = false;
            _list.ShowGroups = false;
            _list.HeaderStyle = ColumnHeaderStyle.Clickable;
            _list.AllowDrop = true;

            BuildIcons();

            // Keeps the ".." row above the entries whatever the user sorted by. Without it the row
            // drifts into the middle of a descending sort — a navigation control that moves depending
            // on which header was last clicked.
            _list.CustomSorter = (column, order) =>
                _list.ListViewItemSorter = new ParentFirstComparer(new ColumnComparer(column, order));

            OLVColumn name = new("Name", nameof(FileSystemEntry.Name))
            {
                Width = 220,
                AspectGetter = o => ((FileSystemEntry)o).Name,
                // Sorted by the controller so both panes agree and the order is testable; the header
                // still sorts, but the default arrival order is already directories-first.
                ImageGetter = o => EntryPresentation.ImageKeyOf((FileSystemEntry)o)
            };

            OLVColumn kind = new(Language.EntryKindColumn, nameof(FileSystemEntry.IsDirectory))
            {
                Width = 70,
                AspectGetter = o => DescribeKind((FileSystemEntry)o)
            };

            OLVColumn size = new("Size", nameof(FileSystemEntry.Length))
            {
                Width = 90,
                TextAlign = HorizontalAlignment.Right,
                // Nullable on purpose: the converter sees only the value, so it cannot tell a
                // directory's zero from an empty file's. The getter has the row and can.
                AspectGetter = o => EntryPresentation.SizeOf((FileSystemEntry)o),
                AspectToStringConverter = value => EntryPresentation.DescribeSize((long?)value)
            };

            OLVColumn modified = new("Modified", nameof(FileSystemEntry.LastWriteTime))
            {
                Width = 130,
                // Stays a DateTime so the column sorts chronologically rather than by the text, which
                // for a dd/MM/yyyy locale would sort by day of the month.
                AspectGetter = o => EntryPresentation.ModifiedOf((FileSystemEntry)o),
                AspectToStringConverter = value =>
                    EntryPresentation.DescribeModified((DateTime?)value, CultureInfo.CurrentCulture)
            };

            List<OLVColumn> columns = [name, kind, size, modified];

            // Only the remote side has permissions worth showing. An empty column on the local pane
            // would imply the information exists and is blank.
            if (_controller.SupportsPermissions)
            {
                columns.Add(new OLVColumn("Permissions", nameof(FileSystemEntry.Permissions))
                {
                    Width = 90,
                    AspectGetter = o => ((FileSystemEntry)o).Permissions
                });
            }

            _list.AllColumns.AddRange(columns);
            _list.Columns.AddRange([.. columns]);
            _list.RebuildColumns();

            _list.ItemActivate += OnItemActivate;
            _list.DragEnter += OnDragEnter;
            _list.DragDrop += OnDragDrop;
        }

        /// <summary>
        /// Builds the list's icons from the bitmaps the application already ships.
        /// </summary>
        /// <remarks>
        /// The Name column's <c>ImageGetter</c> has always returned these keys; what was missing was
        /// an image list for them to resolve against, so no icon was ever drawn. One list per pane
        /// rather than a shared static: sharing risks one pane disposing it while another still draws
        /// from it, which is a crash rather than a few kilobytes.
        /// </remarks>
        private void BuildIcons()
        {
            _icons.ColorDepth = ColorDepth.Depth32Bit;
            _icons.ImageSize = new Size(16, 16);

            _icons.Images.Add(EntryPresentation.FolderImageKey, Properties.Resources.FolderClosed_16x);
            _icons.Images.Add(EntryPresentation.FileImageKey, Properties.Resources.Document_16x);
            _icons.Images.Add(EntryPresentation.ParentImageKey, Properties.Resources.FolderClosed_16x);

            _list.SmallImageList = _icons;
        }

        private static string DescribeKind(FileSystemEntry entry) =>
            EntryPresentation.KindOf(entry) switch
            {
                EntryKind.Folder => Language.EntryKindFolder,
                EntryKind.File => Language.EntryKindFile,
                EntryKind.Link => Language.EntryKindLink,
                _ => string.Empty
            };

        private static void OnDragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnDragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
                return;

            ExternalFilesDropped?.Invoke(this, paths);
        }

        private void BuildStatus()
        {
            _status.Dock = DockStyle.Bottom;
            _status.Height = 20;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Text = string.Empty;
        }

        private async void OnItemActivate(object? sender, EventArgs e)
        {
            if (_list.SelectedObject is not FileSystemEntry entry)
                return;

            if (entry.IsParentNavigation)
                await _controller.NavigateUpAsync();
            else if (entry.IsDirectory)
                await _controller.OpenAsync(entry);
            else
                FileActivated?.Invoke(this, entry);
        }

        private async void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            await _controller.NavigateAsync(_pathBox.Text);
        }

        private void OnEntriesChanged(object? sender, EventArgs e) => RunOnUi(Rebind);

        private void OnBusyChanged(object? sender, EventArgs e) =>
            RunOnUi(() => Cursor = _controller.IsBusy ? Cursors.AppStarting : Cursors.Default);

        private void OnOperationFailed(object? sender, string message) =>
            RunOnUi(() => Failed?.Invoke(this, message));

        private void Rebind()
        {
            _pathBox.Text = _controller.CurrentPath;
            _back.Enabled = _controller.CanGoBack;
            _forward.Enabled = _controller.CanGoForward;

            UpdateConnectionState();

            IReadOnlyList<FileSystemEntry> entries = _controller.Entries;

            // The parent row is merged in here and nowhere else. It is absent from the controller's
            // entries, so the count and total below are right without subtracting it back out.
            _list.SetObjects(_controller.ParentEntry is { } parent ? [parent, .. entries] : entries);

            long totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Length);
            _status.Text = string.Format(CultureInfo.CurrentCulture,
                                         "{0} item(s), {1}", entries.Count,
                                         EntryPresentation.DescribeSize(totalBytes));
        }

        /// <summary>
        /// Marshals to the UI thread. The controller raises its events on whichever thread completed
        /// the operation, which for a remote listing is a background one.
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                    // The handle went away between the check and the call.
                }
            }
            else
            {
                action();
            }
        }

        internal void RequestTransfer() => TransferRequested?.Invoke(this, SelectedEntries);

        /// <summary>Renders a byte count. Kept as a shim for the transfer queue's size column.</summary>
        internal static string DescribeSize(long bytes) => EntryPresentation.DescribeSize(bytes);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.EntriesChanged -= OnEntriesChanged;
                _controller.OperationFailed -= OnOperationFailed;
                _controller.BusyChanged -= OnBusyChanged;

                if (_connection is not null)
                    _connection.ConnectionChanged -= OnConnectionChanged;

                _icons.Dispose();
                _controller.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
