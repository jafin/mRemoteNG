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
        private readonly ToolStripButton _hidden = new();
        private readonly Label _status = new();

        public FilePaneControl(FilePaneController controller, string caption)
        {
            ArgumentNullException.ThrowIfNull(controller);

            _controller = controller;
            Caption = caption;

            BuildToolbar();
            BuildList();
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

        public FilePaneController Controller => _controller;

        /// <summary>Raised when the user asks to transfer the current selection to the other pane.</summary>
        public event EventHandler<IReadOnlyList<FileSystemEntry>>? TransferRequested;

        /// <summary>Raised when the user activates a file (as opposed to opening a directory).</summary>
        public event EventHandler<FileSystemEntry>? FileActivated;

        /// <summary>Raised when an operation fails, so the tab can surface it.</summary>
        public event EventHandler<string>? Failed;

        public IReadOnlyList<FileSystemEntry> SelectedEntries =>
            _list.SelectedObjects.Cast<FileSystemEntry>().ToArray();

        public Task StartAsync() => _controller.NavigateHomeAsync();

        private void BuildToolbar()
        {
            _toolbar.Dock = DockStyle.Top;
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;

            Configure(_back, "◀", "Back", async () => await _controller.GoBackAsync());
            Configure(_forward, "▶", "Forward", async () => await _controller.GoForwardAsync());
            Configure(_up, "▲", "Up", async () => await _controller.NavigateUpAsync());
            Configure(_home, "⌂", "Home", async () => await _controller.NavigateHomeAsync());
            Configure(_refresh, "⟳", "Refresh", async () => await _controller.RefreshAsync());

            _hidden.Text = "•";
            _hidden.ToolTipText = "Show hidden entries";
            _hidden.CheckOnClick = true;
            _hidden.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _hidden.CheckedChanged += (_, _) => _controller.ShowHidden = _hidden.Checked;

            _pathBox.AutoSize = false;
            _pathBox.Width = 320;
            _pathBox.KeyDown += OnPathBoxKeyDown;

            _toolbar.Items.AddRange([_back, _forward, _up, _home, _refresh,
                                     new ToolStripSeparator(), _hidden,
                                     new ToolStripSeparator(), _pathBox]);
        }

        private static void Configure(ToolStripButton button, string glyph, string tooltip, Func<Task> action)
        {
            button.Text = glyph;
            button.ToolTipText = tooltip;
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Click += async (_, _) => await action();
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

            OLVColumn name = new("Name", nameof(FileSystemEntry.Name))
            {
                Width = 220,
                AspectGetter = o => ((FileSystemEntry)o).Name,
                // Sorted by the controller so both panes agree and the order is testable; the header
                // still sorts, but the default arrival order is already directories-first.
                ImageGetter = o => ((FileSystemEntry)o).IsDirectory ? "folder" : "file"
            };

            OLVColumn size = new("Size", nameof(FileSystemEntry.Length))
            {
                Width = 90,
                TextAlign = HorizontalAlignment.Right,
                AspectGetter = o => ((FileSystemEntry)o).Length,
                AspectToStringConverter = value => DescribeSize((long)(value ?? 0L))
            };

            OLVColumn modified = new("Modified", nameof(FileSystemEntry.LastWriteTime))
            {
                Width = 130,
                AspectGetter = o => ((FileSystemEntry)o).LastWriteTime
            };

            List<OLVColumn> columns = [name, size, modified];

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

            if (entry.IsDirectory)
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

            IReadOnlyList<FileSystemEntry> entries = _controller.Entries;
            _list.SetObjects(entries);

            long totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Length);
            _status.Text = string.Format(CultureInfo.CurrentCulture,
                                         "{0} item(s), {1}", entries.Count, DescribeSize(totalBytes));
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

        internal static string DescribeSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Format(CultureInfo.CurrentCulture, "{0} {1}", bytes, units[unit])
                : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", value, units[unit]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.EntriesChanged -= OnEntriesChanged;
                _controller.OperationFailed -= OnOperationFailed;
                _controller.BusyChanged -= OnBusyChanged;
                _controller.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
