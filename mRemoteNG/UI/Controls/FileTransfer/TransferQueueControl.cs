using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.FileTransfer;
using mRemoteNG.Themes;

namespace mRemoteNG.UI.Controls.FileTransfer;

/// <summary>
/// The transfer queue: what is waiting, what is running, what failed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TransferQueueControl : UserControl
{
    private readonly TransferQueue _queue;
    private readonly ObjectListView _list = new();
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripButton _cancelSelected = new();
    private readonly ToolStripButton _cancelAll = new();
    private readonly ToolStripButton _clearFinished = new();
    private readonly ToolStripLabel _summary = new();

    /// <summary>
    /// Guards against repainting the list on every progress report. A transfer reports each
    /// buffer, which is hundreds of times a second — rebuilding rows at that rate would spend
    /// more time drawing the queue than moving the file.
    /// </summary>
    private readonly System.Windows.Forms.Timer _repaint = new() { Interval = 250 };

    private bool _dirty;

    public TransferQueueControl(TransferQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;

        BuildToolbar();
        BuildList();

        Controls.Add(_list);
        Controls.Add(_toolbar);

        _queue.ItemChanged += OnItemChanged;
        _repaint.Tick += OnRepaintTick;
        _repaint.Start();
    }

    /// <summary>Applies the active theme, when it supplies an extended palette.</summary>
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
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;

        _cancelSelected.Text = "Cancel";
        _cancelSelected.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _cancelSelected.Click += (_, _) =>
        {
            foreach (TransferItem item in _list.SelectedObjects.Cast<TransferItem>())
                _queue.Cancel(item);
        };

        _cancelAll.Text = "Cancel all";
        _cancelAll.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _cancelAll.Click += (_, _) => _queue.CancelAll();

        _clearFinished.Text = "Clear finished";
        _clearFinished.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _clearFinished.Click += (_, _) =>
        {
            _queue.ClearFinished();
            Rebind();
        };

        _toolbar.Items.AddRange([_cancelSelected, _cancelAll, _clearFinished,
            new ToolStripSeparator(), _summary]);
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.ShowGroups = false;
        _list.HeaderStyle = ColumnHeaderStyle.Clickable;

        OLVColumn direction = new("", nameof(TransferItem.Direction))
        {
            Width = 30,
            AspectGetter = o => Glyph((TransferItem)o)
        };

        OLVColumn source = new("Source", nameof(TransferItem.SourcePath))
        {
            Width = 260,
            AspectGetter = o => ((TransferItem)o).SourcePath
        };

        OLVColumn destination = new("Destination", nameof(TransferItem.DestinationPath))
        {
            Width = 260,
            // Blank for a deletion: there is nowhere the entry is going.
            AspectGetter = o => ((TransferItem)o).DestinationPath
        };

        OLVColumn size = new("Size", nameof(TransferItem.Size))
        {
            Width = 80,
            TextAlign = HorizontalAlignment.Right,
            AspectGetter = o => ((TransferItem)o).Size,
            AspectToStringConverter = v => FilePaneControl.DescribeSize((long)(v ?? 0L))
        };

        OLVColumn progress = new("Progress", nameof(TransferItem.Transferred))
        {
            Width = 90,
            AspectGetter = o => ((TransferItem)o).Fraction,
            AspectToStringConverter = v => v is double fraction
                ? string.Format(CultureInfo.CurrentCulture, "{0:P0}", fraction)
                : string.Empty
        };

        OLVColumn status = new("Status", nameof(TransferItem.Status))
        {
            Width = 100,
            AspectGetter = o => Describe((TransferItem)o)
        };

        OLVColumn[] columns = [direction, source, destination, size, progress, status];
        _list.AllColumns.AddRange(columns);
        _list.Columns.AddRange(columns);
        _list.RebuildColumns();
    }

    /// <summary>
    /// The one-character mark in the leading column: which way a transfer is going, or that the
    /// item removes something rather than moving it.
    /// </summary>
    internal static string Glyph(TransferItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind == TransferOperationKind.Delete ? "✕"
            : item.Direction == TransferDirection.Upload ? "↑"
            : "↓";
    }

    private static string Describe(TransferItem item) =>
        item.Status == TransferStatus.Failed && !string.IsNullOrEmpty(item.FailureReason)
            ? string.Format(CultureInfo.CurrentCulture, "Failed: {0}", item.FailureReason)
            : item.Status.ToString();

    /// <summary>
    /// Only flags the view dirty. The queue raises this from a background thread and as often as
    /// a buffer is written, so the actual repaint is left to the timer.
    /// </summary>
    private void OnItemChanged(object? sender, TransferItem item) => _dirty = true;

    private void OnRepaintTick(object? sender, EventArgs e)
    {
        if (!_dirty || IsDisposed || !IsHandleCreated)
            return;

        _dirty = false;
        Rebind();
    }

    private void Rebind()
    {
        _list.SetObjects(_queue.Items);

        int running = _queue.Queued.Count;
        int failed = _queue.Failed.Count;

        _summary.Text = failed > 0
            ? string.Format(CultureInfo.CurrentCulture, "{0} pending, {1} failed", running, failed)
            : string.Format(CultureInfo.CurrentCulture, "{0} pending", running);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repaint.Stop();
            _repaint.Tick -= OnRepaintTick;
            _repaint.Dispose();
            _queue.ItemChanged -= OnItemChanged;
        }

        base.Dispose(disposing);
    }
}