using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.App;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Properties;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Themes;
using mRemoteNG.Tools.Clipboard;
using mRemoteNG.Tree;
using mRemoteNG.Tree.ClickHandlers;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI.Forms;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.UI.Controls.ConnectionTree;

[SupportedOSPlatform("windows")]
public partial class ConnectionTree : TreeListView, IConnectionTree
{
    private readonly ConnectionTreeDragAndDropHandler _dragAndDropHandler = new();
    private readonly PuttySessionsManager _puttySessionsManager = PuttySessionsManager.Instance;
    private readonly StatusImageList _statusImageList = new();
    private ThemeManager _themeManager;

    private readonly ConnectionTreeSearchTextFilter _connectionTreeSearchTextFilter = new();
    private List<object>? _preFilterExpandedObjects;
    private bool _columnAutoResizeSuspended;

    private bool _nodeInEditMode;
    private bool _allowEdit;
    private ISlowClickRenameHandler? _slowClickRenameHandler;
    private ConnectionContextMenu _contextMenu = null!;
    private ConnectionTreeModel? _connectionTreeModel;
    private List<ConnectionInfo> _clipboardNodes = [];

    // When the model has exactly one connection root ("Connections"), that root is hidden and its
    // children are shown at the top level — the pane heading already says "Connections", so the
    // node is redundant and costs every entry an extra indent level. Null when not promoting
    // (e.g. multiple connection roots). The root stays in the model; only the view is re-rooted.

    public ConnectionInfo SelectedNode => (ConnectionInfo)SelectedObject;

    public NodeSearcher? NodeSearcher { get; private set; }

    public IConfirm<ConnectionInfo> NodeDeletionConfirmer { get; set; } = new AlwaysConfirmYes();

    public IEnumerable<IConnectionTreeDelegate> PostSetupActions { get; set; } = [];

    public ITreeNodeClickHandler<ConnectionInfo> DoubleClickHandler { get; set; } = new TreeNodeCompositeClickHandler();

    public ITreeNodeClickHandler<ConnectionInfo> SingleClickHandler { get; set; } = new TreeNodeCompositeClickHandler();

    public ITreeNodeClickHandler<ConnectionInfo> MiddleClickHandler { get; set; } = new TreeNodeCompositeClickHandler();

    public ConnectionTreeModel ConnectionTreeModel
    {
        get { return _connectionTreeModel!; }
        set
        {
            if (_connectionTreeModel == value)
            {
                return;
            }

            if (_connectionTreeModel != null)
                UnregisterModelUpdateHandlers(_connectionTreeModel);
            _connectionTreeModel = value;
            PopulateTreeView(value);
        }
    }

    public ConnectionTree()
    {
        InitializeComponent();
        SetupConnectionTreeView();
        ConfigureCompactAppearance();
        UseOverlays = false;
        UseWaitCursorWhenExpanding = false;
        _themeManager = ThemeManager.getInstance();
        _themeManager.ThemeChanged += ThemeManagerOnThemeChanged;
        ApplyTheme();
    }

    /// <summary>
    /// Compact appearance for the connection tree to minimize horizontal space:
    /// chevron (triangle) expanders instead of boxed +/- and tighter per-level indentation.
    /// Sibling guide lines are kept. A custom renderer draws the chevron larger than the
    /// (narrow) indent so it stays legible.
    /// </summary>
    private void ConfigureCompactAppearance()
    {
        TreeColumnRenderer = new CompactTreeRenderer { UseTriangles = true };

        // PIXELS_PER_LEVEL is a static on the vendored TreeRenderer. ConnectionTree is the only
        // TreeListView in the app, so narrowing it here affects just this tree.
        BrightIdeasSoftware.TreeListView.TreeRenderer.PIXELS_PER_LEVEL = 12;
    }

    /// <summary>
    /// Tree renderer that draws the expansion chevron a few pixels larger than the glyph box.
    /// PIXELS_PER_LEVEL drives both indentation and glyph size, so a narrow 12px indent would
    /// otherwise leave a tiny chevron; this enlarges only the drawn glyph, not the indent.
    /// </summary>
    private sealed class CompactTreeRenderer : BrightIdeasSoftware.TreeListView.TreeRenderer
    {
        private const int GlyphInflate = 3;

        protected override void DrawExpansionGlyph(System.Drawing.Graphics g, System.Drawing.Rectangle r, bool isExpanded)
        {
            r.Inflate(GlyphInflate, GlyphInflate);
            base.DrawExpansionGlyph(g, r, isExpanded);
        }
    }

    private void ThemeManagerOnThemeChanged()
    {
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (!_themeManager.ActiveAndExtended)
            return;

        var themePalette = _themeManager.ActiveTheme.ExtendedPalette;
        if (themePalette == null) return;

        BackColor = themePalette.getColor("TreeView_Background");
        ForeColor = themePalette.getColor("TreeView_Foreground");
        SelectedBackColor = themePalette.getColor("Treeview_SelectedItem_Active_Background");
        SelectedForeColor = themePalette.getColor("Treeview_SelectedItem_Active_Foreground");
        UnfocusedSelectedBackColor = themePalette.getColor("Treeview_SelectedItem_Inactive_Background");
        UnfocusedSelectedForeColor = themePalette.getColor("Treeview_SelectedItem_Inactive_Foreground");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _statusImageList?.Dispose();
            _slowClickRenameHandler?.Dispose();

            _themeManager.ThemeChanged -= ThemeManagerOnThemeChanged;
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int WM_LBUTTONDOWN = 0x0201;

        if (DevLog.IsEnabled && (m.Msg == WM_MOUSEACTIVATE || m.Msg == WM_LBUTTONDOWN || m.Msg == 0x0007))
            DevLog.Write($"Msg=0x{m.Msg:X4} Focused={Focused} SelectedObject={SelectedObject}");

        if (m.Msg == WM_MOUSEACTIVATE)
        {
            // Identify the click target BEFORE Focus() triggers WM_SETFOCUS,
            // so the scroll-restore handler knows not to snap back (#68).
            var clientPt = PointToClient(MousePosition);
            var hit = OlvHitTest(clientPt.X, clientPt.Y);
            _pendingClickTarget = hit.Item?.RowObject as ConnectionInfo;
            DevLog.Write($"WM_MOUSEACTIVATE: pendingClickTarget={_pendingClickTarget?.Name}");

            // Freeze painting during the focus battle to avoid visible scroll/selection flicker
            const int WM_SETREDRAW = 0x000B;
            NativeMethods.SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            Focus();
            BeginInvoke(() =>
            {
                NativeMethods.SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                Invalidate(true);
            });
        }

        // WM_SETFOCUS: previously had scroll-restore logic (#1925) but it
        // breaks tree navigation when RDP ActiveX controls cause focus
        // oscillation — the scroll snaps back and makes the tree unusable.
        // Removed: let ListView handle focus scrolling naturally (#68).
        base.WndProc(ref m);
    }

    #region ConnectionTree Setup

    private void SetupConnectionTreeView()
    {
        SetSmallImageList(_statusImageList.ImageList);
        AddColumns(_statusImageList.ImageGetter);
        LinkModelToView();
        _contextMenu = new ConnectionContextMenu(this);
        ContextMenuStrip = _contextMenu;
        SetupDropSink();
        SetEventHandlers();
        SetupSlowClickRename();
    }

    internal void SetupSlowClickRename()
    {
        _slowClickRenameHandler?.Dispose();
        _slowClickRenameHandler = Properties.Settings.Default.SlowClickRenameEnabled
            ? new SlowClickRenameHandler(
                new SlowClickRenameTimer(SystemInformation.DoubleClickTime),
                RenameSelectedNode,
                () => SelectedNode)
            : null;
    }

    private void AddColumns(ImageGetterDelegate imageGetterDelegate)
    {
        Columns.Add(new NameColumn(imageGetterDelegate));
        Columns.Add(new DescriptionColumn());
    }

    private void LinkModelToView()
    {
        CanExpandGetter = item =>
        {
            var itemAsContainer = item as ContainerInfo;
            return itemAsContainer?.Children.Count > 0;
        };
        ChildrenGetter = item => ((ContainerInfo)item).Children;
    }

    private void SetupDropSink()
    {
        DropSink = new SimpleDropSink
        {
            CanDropBetween = true
        };
    }

    private void SetEventHandlers()
    {
        Collapsed += (sender, args) =>
        {
            if (args.Model is not ContainerInfo container) return;
            container.IsExpanded = false;
            AutoResizeColumn(Columns[0]);
        };
        Expanded += (sender, args) =>
        {
            if (args.Model is not ContainerInfo container) return;
            container.IsExpanded = true;
            AutoResizeColumn(Columns[0]);
        };
        Expanding += OnExpanding;
        SelectionChanged += TvConnections_AfterSelect;
        MouseDown += OnMouse_Down;
        MouseDoubleClick += OnMouse_DoubleClick;
        MouseClick += OnMouse_SingleClick;
        MouseClick += OnMouse_MiddleClick;
        CellToolTipShowing += TvConnections_CellToolTipShowing;
        ModelCanDrop += _dragAndDropHandler.OnModelCanDrop;
        ModelDropped += _dragAndDropHandler.OnModelDropped;
        BeforeLabelEdit += OnBeforeLabelEdit;
        AfterLabelEdit += OnAfterLabelEdit;
        FormatCell += ConnectionTree_FormatCell;
        SizeChanged += OnTreeSizeChanged;
    }

    private void OnExpanding(object? sender, TreeBranchExpandingEventArgs e)
    {
        if (e.Model is not ContainerInfo container) return;
        if (string.IsNullOrEmpty(container.ContainerPassword)) return;
        if (container.IsUnlocked) return;

        using FrmPassword passwordForm = new(container.Name, false);
        if (passwordForm.ShowDialog() == DialogResult.OK)
        {
            var key = passwordForm.GetKey();
            if (key.Any() && key.First().ConvertToUnsecureString() == container.ContainerPassword)
            {
                container.IsUnlocked = true;
            }
            else
            {
                e.Canceled = true;
                MessageBox.Show("Incorrect password.", "Security", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            e.Canceled = true;
        }
    }

    // Widest Name content measured by AutoResizeColumn, before the cap is applied. Cached so a
    // pane resize can re-apply the cap without re-measuring every row. Zero means "not measured
    // yet" — never push a zero width onto the column.
    private int _desiredNameColumnWidth;

    /// <summary>
    /// Sizes the Name column to its content, capped at the tree's own width so an over-long name
    /// ellipsizes (with a full-name tooltip) instead of forcing a horizontal scrollbar. A name
    /// that fits within the pane is shown in full; the Description column fills whatever the Name
    /// column leaves.
    /// </summary>
    private void AutoResizeColumn(ColumnHeader column)
    {
        if (InvokeRequired)
        {
            Invoke((MethodInvoker)(() => AutoResizeColumn(column)));
            return;
        }

        // Nothing to measure (e.g. a transient empty state mid-rebuild): keep the current width
        // rather than collapsing the column.
        if (Items.Count == 0)
            return;

        var longestIndentationAndTextWidth = 0;
        for (var i = 0; i < Items.Count; i++)
        {
            var rowIndentation = Items[i].Position.X;
            var rowTextWidth = TextRenderer.MeasureText(Items[i].Text, Font).Width;
            longestIndentationAndTextWidth = Math.Max(rowIndentation + rowTextWidth, longestIndentationAndTextWidth);
        }

        const int padding = 10;
        _desiredNameColumnWidth = longestIndentationAndTextWidth + SmallImageSize.Width + padding;
        column.Width = CapNameColumnWidth(_desiredNameColumnWidth);
    }

    // Sliver of the pane the Name column leaves to Description, so the fill column never
    // collapses to nothing and its header stays grabbable.
    private const int DescriptionMinWidth = 40;

    /// <summary>
    /// Clamps a desired Name-column width to the tree width so it never forces a horizontal
    /// scrollbar, leaving <see cref="DescriptionMinWidth"/> for the fill column. A name that
    /// fits keeps its full width.
    /// </summary>
    private int CapNameColumnWidth(int desiredWidth)
    {
        var clientWidth = ClientSize.Width;
        if (clientWidth <= 0)
            return desiredWidth;

        // In a pane too narrow to give both columns their due, Name keeps at least half —
        // it is the column that identifies the row.
        var cap = Math.Max(clientWidth - DescriptionMinWidth, clientWidth / 2);
        return Math.Min(desiredWidth, cap);
    }

    private void OnTreeSizeChanged(object? sender, EventArgs e)
    {
        // Re-apply the cap so the Name column can reclaim/yield width as the pane resizes,
        // without re-measuring rows. Skip until a real width has been measured.
        if (Columns.Count > 0 && _desiredNameColumnWidth > 0)
            Columns[0].Width = CapNameColumnWidth(_desiredNameColumnWidth);
    }

    /// <summary>
    /// Computes the objects shown at the top level of the tree: the model's roots, always.
    /// </summary>
    /// <remarks>
    /// A lone connection root used to be hidden and replaced by its children, on the grounds that
    /// the pane heading already reads "Connections" and the node cost every entry an indent level.
    /// It also carried the only user interface for the connection file's master password — the
    /// <c>Password</c> property on the root, edited through the property grid — so hiding the node
    /// removed the ability to set or remove one. Nothing else exposes it: the other password
    /// prompts all verify a password that is already set.
    ///
    /// The indent saving is not worth the security control. The rest of the compact tree — chevron
    /// expanders, the tighter indent, the sizing of the Name column — is unaffected.
    /// </remarks>
    private IList<ConnectionInfo> ComputeViewRoots(ConnectionTreeModel model)
    {
        var roots = model.RootNodes;
        _viewRootSources = [.. roots];
        return [.. roots.Cast<ConnectionInfo>()];
    }

    // The model's root nodes as of the last ComputeViewRoots call. A collection change that
    // alters this set has to rebuild the top level rather than add/remove in place: the set
    // decides both which objects are view roots and whether a lone connection root is promoted.
    private List<ContainerInfo> _viewRootSources = [];

    /// <summary>
    /// Whether the model's root nodes differ from the set the view was last built from — i.e.
    /// whether the change being handled added or removed a root node. This cannot be inferred
    /// from the changed item itself: <see cref="ContainerInfo.RemoveChild"/> clears the child's
    /// Parent before raising, so a removed child and a removed root look identical.
    /// </summary>
    private bool RootNodesChanged() =>
        _connectionTreeModel != null && !_connectionTreeModel.RootNodes.SequenceEqual(_viewRootSources);

    /// <summary>
    /// Rebuilds the top-level objects (e.g. after the hidden root's children are reordered)
    /// while preserving the current selection and expansion state.
    /// </summary>
    private void RefreshViewRootsPreservingState()
    {
        if (_connectionTreeModel == null)
            return;

        var expanded = ExpandedObjects.Cast<object>().ToList();
        var selected = SelectedObjects;
        SetObjects(ComputeViewRoots(_connectionTreeModel));
        RebuildAll(selected, expanded, null);
    }

    private void PopulateTreeView(ConnectionTreeModel newModel)
    {
        BeginUpdate();
        try
        {
            SetObjects(ComputeViewRoots(newModel));
            RegisterModelUpdateHandlers(newModel);
            NodeSearcher = new NodeSearcher(newModel);
            ExecutePostSetupActions();
        }
        finally
        {
            EndUpdate();
        }
        AutoResizeColumn(Columns[0]);
    }

    private void RegisterModelUpdateHandlers(ConnectionTreeModel newModel)
    {
        _puttySessionsManager.PuttySessionsCollectionChanged += OnPuttySessionsCollectionChanged;
        newModel.CollectionChanged += HandleCollectionChanged;
        newModel.PropertyChanged += HandleCollectionPropertyChanged;
    }

    private void UnregisterModelUpdateHandlers(ConnectionTreeModel oldConnectionTreeModel)
    {
        _puttySessionsManager.PuttySessionsCollectionChanged -= OnPuttySessionsCollectionChanged;

        if (oldConnectionTreeModel == null)
            return;

        oldConnectionTreeModel.CollectionChanged -= HandleCollectionChanged;
        oldConnectionTreeModel.PropertyChanged -= HandleCollectionPropertyChanged;
    }

    private void OnPuttySessionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshObjects(GetRootPuttyNodes().ToList());
    }

    private void HandleCollectionPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        // for some reason property changed events are getting triggered twice for each changed property. should be just once. cant find source of duplication
        // Removed "TO DO" from above comment. Per #142 it apperas that this no longer occurs with ObjectListView 2.9.1
        var property = propertyChangedEventArgs.PropertyName;
        if (property != nameof(ConnectionInfo.Name)
            && property != nameof(ConnectionInfo.OpenConnections)
            && property != nameof(ConnectionInfo.Icon)
            && property != nameof(ConnectionInfo.Description)
            && property != nameof(ConnectionInfo.HostReachabilityStatus))
        {
            return;
        }

        if (sender is not ConnectionInfo senderAsConnectionInfo)
            return;

        // HostStatusMonitor fires from background thread — marshal to UI
        if (InvokeRequired)
        {
            BeginInvoke(() =>
            {
                RefreshObject(senderAsConnectionInfo);
                AutoResizeColumn(Columns[0]);
            });
            return;
        }

        RefreshObject(senderAsConnectionInfo);
        AutoResizeColumn(Columns[0]);
    }

    private void ExecutePostSetupActions()
    {
        foreach (var action in PostSetupActions)
        {
            action.Execute(this);
        }
    }

    #endregion

    #region ConnectionTree Behavior

    public RootNodeInfo GetRootConnectionNode()
    {
        return (RootNodeInfo)ConnectionTreeModel.RootNodes.First(item => item is RootNodeInfo);
    }

    public new void Invoke(Action action)
    {
        Invoke((Delegate)action);
    }

    public void InvokeExpand(object model)
    {
        Invoke(() => Expand(model));
    }

    public void InvokeRebuildAll(bool preserveState)
    {
        Invoke(() => RebuildAll(preserveState));
    }

    public IEnumerable<RootPuttySessionsNodeInfo> GetRootPuttyNodes()
    {
        return Objects.OfType<RootPuttySessionsNodeInfo>();
    }

    private static bool IsReadOnly => Properties.OptionsDBsPage.Default.SQLReadOnly;

    public void AddConnection()
    {
        if (IsReadOnly) return;
        try
        {
            AddNode(new ConnectionInfo());
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("UI.Window.Tree.AddConnection() failed.", ex);
        }
    }

    public void AddFolder()
    {
        if (IsReadOnly) return;
        try
        {
            AddNode(new ContainerInfo());
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.ErrorAddFolderFailed, ex);
        }
    }

    public void AddEntity()
    {
        if (IsReadOnly) return;
        try
        {
            ContainerInfo entity = new() { IsEntity = true, Name = "New Entity" };
            AddNode(entity);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("Failed to add entity", ex);
        }
    }

    public void AddRootFolder()
    {
        if (IsReadOnly) return;
        try
        {
            ContainerInfo newFolder = new();
            newFolder.IsRoot = true;
            DefaultConnectionInfo.Instance.SaveTo(newFolder);
            DefaultConnectionInheritance.SaveTo(newFolder.Inheritance);
            if (Settings.Default.InhDefaultEverythingInherited)
                newFolder.Inheritance.TurnOnInheritanceCompletely();

            ConnectionTreeModel.AddRootNode(newFolder);

            SelectObject(newFolder, true);
            EnsureModelVisible(newFolder);
            _allowEdit = true;
            SelectedItem.BeginEdit();
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.ErrorAddFolderFailed, ex);
        }
    }

    private void AddNode(ConnectionInfo newNode)
    {
        if (SelectedNode?.GetTreeNodeType() == TreeNodeType.PuttyRoot ||
            SelectedNode?.GetTreeNodeType() == TreeNodeType.PuttySession)
            return;

        // the new node will survive filtering if filtering is active
        _connectionTreeSearchTextFilter.SpecialInclusionList.Add(newNode);

        // use root node if no node is selected
        var parentNode = SelectedNode ?? GetRootConnectionNode();
        DefaultConnectionInfo.Instance.SaveTo(newNode);
        DefaultConnectionInheritance.SaveTo(newNode.Inheritance);
        if (Settings.Default.InhDefaultEverythingInherited)
            newNode.Inheritance.TurnOnInheritanceCompletely();
        var selectedContainer = parentNode as ContainerInfo;
        var parent = selectedContainer ?? parentNode.Parent;
        if (parent == null) return;
        newNode.SetParent(parent);
        // Default the new node's Panel to the parent folder's Panel (#1982)
        if (!newNode.Inheritance.Panel)
        {
            var parentPanel = parent.Panel;
            if (!string.IsNullOrEmpty(parentPanel))
                newNode.Panel = parentPanel;
        }
        Expand(parent);
        SelectObject(newNode, true);
        EnsureModelVisible(newNode);
        _allowEdit = true;
        SelectedItem.BeginEdit();
    }

    internal List<ConnectionInfo> GetSelectedNodes()
    {
        var selectedNodes = SelectedObjects?.OfType<ConnectionInfo>().Distinct().ToList() ?? [];

        if (selectedNodes.Count == 0 && SelectedNode != null)
            selectedNodes.Add(SelectedNode);

        return selectedNodes;
    }

    private static void ExecuteInBatchedSaveContext(Action action)
    {
        Runtime.ConnectionsService.BeginBatchingSaves();

        try
        {
            action();
        }
        finally
        {
            Runtime.ConnectionsService.EndBatchingSaves();
        }
    }

    public void DuplicateSelectedNode()
    {
        if (IsReadOnly) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var selectedNode in GetSelectedNodes())
            {
                var selectedNodeType = selectedNode.GetTreeNodeType();
                if (selectedNodeType != TreeNodeType.Connection && selectedNodeType != TreeNodeType.Container)
                    continue;

                var newNode = selectedNode.Clone();
                if (selectedNode.Parent == null) continue;
                selectedNode.Parent.AddChildBelow(newNode, selectedNode);
                newNode.Parent?.SetChildBelow(newNode, selectedNode);
            }
        });
    }

    public bool HasClipboardNodes => _clipboardNodes.Count > 0;

    public void CopySelectedNodes()
    {
        _clipboardNodes =
        [
            .. GetSelectedNodes()
                .Where(n =>
                {
                    var type = n.GetTreeNodeType();
                    return type == TreeNodeType.Connection || type == TreeNodeType.Container;
                })
        ];
    }

    public void PasteNodes()
    {
        if (IsReadOnly) return;
        if (_clipboardNodes.Count == 0) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var copiedNode in _clipboardNodes)
            {
                var newNode = copiedNode.Clone();
                if (SelectedNode is ContainerInfo container)
                {
                    container.AddChild(newNode);
                }
                else if (SelectedNode?.Parent != null)
                {
                    SelectedNode.Parent.AddChildBelow(newNode, SelectedNode);
                }
            }
        });
    }

    public void CreateLinkToSelectedNode()
    {
        if (IsReadOnly) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var selectedNode in GetSelectedNodes())
            {
                if (selectedNode.GetTreeNodeType() != TreeNodeType.Connection)
                    continue;

                if (selectedNode.Parent == null)
                    continue;

                var newNode = selectedNode.Clone();
                var sourceNode = ConnectionTreeModel.ResolveLinkedConnection(selectedNode) ?? selectedNode;
                newNode.LinkedConnectionId = sourceNode.ConstantID;

                selectedNode.Parent.AddChildBelow(newNode, selectedNode);
                newNode.Parent?.SetChildBelow(newNode, selectedNode);
            }
        });
    }

    public void RenameSelectedNode()
    {
        if (IsReadOnly) return;
        if (SelectedItem == null) return;
        _slowClickRenameHandler?.Cancel();
        _allowEdit = true;
        SelectedItem.BeginEdit();
    }

    public void DeleteSelectedNode()
    {
        if (IsReadOnly) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var selectedNode in GetSelectedNodes())
            {
                if (selectedNode is RootNodeInfo rootNode)
                {
                    if (ConnectionTreeModel.RootNodes.Count > 1)
                    {
                        if (!NodeDeletionConfirmer.Confirm(selectedNode)) return;
                        ConnectionTreeModel.RemoveRootNode(rootNode);
                    }
                    continue;
                }

                if (selectedNode is PuttySessionInfo) continue;
                if (selectedNode.Parent == null) continue;
                if (!NodeDeletionConfirmer.Confirm(selectedNode)) return;
                mRemoteNG.Tree.ConnectionTreeModel.DeleteNode(selectedNode);
            }
        });
    }

    /// <summary>
    /// Copies the Hostname of the selected connection (or the Name of
    /// the selected container) to the given <see cref="IClipboard"/>.
    /// </summary>
    /// <param name="clipboard"></param>
    public void CopyHostnameSelectedNode(IClipboard clipboard)
    {
        if (SelectedNode == null)
            return;

        var textToCopy = SelectedNode.IsContainer ? SelectedNode.Name : SelectedNode.Hostname;

        if (string.IsNullOrEmpty(textToCopy))
            return;

        clipboard.SetText(textToCopy);
    }

    public void SortRecursive(ConnectionInfo sortTarget, ListSortDirection sortDirection)
    {
        if (IsReadOnly) return;
        sortTarget ??= GetRootConnectionNode();

        // Unbalanced, this leaves batching on for the rest of the session and every later
        // save is silently swallowed into the deferred flag instead of reaching disk.
        ExecuteInBatchedSaveContext(() =>
        {
            if (sortTarget is ContainerInfo sortTargetAsContainer)
                sortTargetAsContainer.SortRecursive(sortDirection);
            else
                SelectedNode?.Parent?.SortRecursive(sortDirection);
        });
    }

    public void SortSelectedNodesRecursive(ListSortDirection sortDirection)
    {
        if (IsReadOnly) return;
        var sortTargets = GetSelectedNodes()
            .Select(selectedNode => selectedNode as ContainerInfo ?? selectedNode.Parent)
            .Where(sortTarget => sortTarget != null)
            .Distinct()
            .Cast<ContainerInfo>()
            .ToList();

        if (sortTargets.Count == 0)
        {
            SortRecursive(SelectedNode, sortDirection);
            return;
        }

        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var sortTarget in sortTargets)
            {
                sortTarget.SortRecursive(sortDirection);
            }
        });
    }

    public void SortSelectedNodesByTagRecursive(ListSortDirection sortDirection)
    {
        if (IsReadOnly) return;
        var sortTargets = GetSelectedNodes()
            .Select(selectedNode => selectedNode as ContainerInfo ?? selectedNode.Parent)
            .Where(sortTarget => sortTarget != null)
            .Distinct()
            .Cast<ContainerInfo>()
            .ToList();

        if (sortTargets.Count == 0)
        {
            if (GetRootConnectionNode() is ContainerInfo root)
                ExecuteInBatchedSaveContext(() => root.SortOnRecursive(ci => ci.EnvironmentTags ?? "", sortDirection));
            return;
        }

        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var sortTarget in sortTargets)
            {
                sortTarget.SortOnRecursive(ci => ci.EnvironmentTags ?? "", sortDirection);
            }
        });
    }

    public void MoveSelectedNodesUp()
    {
        if (IsReadOnly) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var parentGroup in
                     GetSelectedNodes()
                         .Where(selectedNode => selectedNode.Parent != null)
                         .GroupBy(selectedNode => selectedNode.Parent!))
            {
                foreach (var selectedNode in parentGroup.OrderBy(selectedNode => parentGroup.Key.Children.IndexOf(selectedNode)))
                {
                    parentGroup.Key.PromoteChild(selectedNode);
                }
            }
        });
    }

    public void MoveSelectedNodesDown()
    {
        if (IsReadOnly) return;
        ExecuteInBatchedSaveContext(() =>
        {
            foreach (var parentGroup in
                     GetSelectedNodes()
                         .Where(selectedNode => selectedNode.Parent != null)
                         .GroupBy(selectedNode => selectedNode.Parent!))
            {
                foreach (var selectedNode in parentGroup.OrderByDescending(selectedNode => parentGroup.Key.Children.IndexOf(selectedNode)))
                {
                    parentGroup.Key.DemoteChild(selectedNode);
                }
            }
        });
    }

    /// <summary>
    /// Expands all tree objects and recalculates the
    /// column widths.
    /// </summary>
    public override void ExpandAll()
    {
        base.ExpandAll();
        AutoResizeColumn(Columns[0]);
    }

    /// <summary>
    /// Expands all tree objects. If filtering is active, it ensures that
    /// when the filter is removed, all objects remain expanded.
    /// </summary>
    public void UserExpandAll()
    {
        ExpandAll();

        if (IsFiltering)
        {
            // Update the pre-filter expanded state to include all containers
            // so that when the filter is cleared, everything stays expanded.
            var allContainers = new List<object>();
            if (ConnectionTreeModel != null)
            {
                foreach (var root in ConnectionTreeModel.RootNodes)
                {
                    allContainers.Add(root);
                    allContainers.AddRange(root.GetRecursiveChildList().OfType<ContainerInfo>());
                }
            }
            _preFilterExpandedObjects = allContainers;
        }
    }

    /// <summary>
    /// Filters tree items based on the given <see cref="filterText"/>
    /// </summary>
    /// <param name="filterText">The text to filter by</param>
    public void ApplyFilter(string filterText)
    {
        if (!UseFiltering)
        {
            // ExpandedObjects is a live view over the tree model's internal map, not a
            // snapshot. Copy it, otherwise the ExpandAll() below rewrites the "pre-filter"
            // state and RemoveFilter() ends up restoring from a collection it just cleared.
            _preFilterExpandedObjects = [.. ExpandedObjects.Cast<object>()];
        }

        UseFiltering = true;
        _connectionTreeSearchTextFilter.FilterText = filterText;
        ModelFilter = _connectionTreeSearchTextFilter;
        ExpandAll();
    }

    /// <summary>
    /// Removes all item filtering from the connection tree
    /// </summary>
    public void RemoveFilter()
    {
        // Clearing the filter takes several passes: dropping UseFiltering and the column
        // filter each run UpdateFiltering, then the tree is rebuilt below. Batch them —
        // hold painting for the whole method so the intermediate states never reach the
        // screen, and collapse the per-pass column auto-resize (which measures every
        // visible row) into the single call at the end.
        BeginUpdate();
        _columnAutoResizeSuspended = true;
        try
        {
            UseFiltering = false;
            ResetColumnFiltering();

            if (_preFilterExpandedObjects != null)
            {
                // Assigning ExpandedObjects only updates the tree model's expansion map; the
                // branch structure and the virtual list size keep the filtered layout until
                // the tree is rebuilt. Without the rebuild the tree renders the wrong
                // expand/collapse state and IndexOf() hands out row indexes that no longer
                // exist, which makes EnsureVisible throw. RebuildAll restores expansion and
                // the row list together.
                RebuildAll(SelectedObjects, _preFilterExpandedObjects, null);
                _preFilterExpandedObjects = null;
            }
        }
        finally
        {
            _columnAutoResizeSuspended = false;
            EndUpdate();
        }

        if (Columns.Count > 0)
            AutoResizeColumn(Columns[0]);
    }

    private void HandleCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
    {
        // Suspend redraw for the whole update. Below we drop the model filter
        // (ResetColumnFiltering) so RefreshObjects can't throw, then restore it — without
        // BeginUpdate/EndUpdate the tree paints the intermediate unfiltered state, so a burst
        // of collection changes (e.g. while a connection opens) makes it visibly flash between
        // the full and filtered views (#144).
        BeginUpdate();
        try
        {
            // disable filtering if necessary. prevents RefreshObjects from
            // throwing an exception
            var filteringEnabled = IsFiltering;
            var filter = ModelFilter;
            if (filteringEnabled)
            {
                ResetColumnFiltering();
            }

            if (sender is ConnectionTreeModel)
            {
                switch (args.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        // A root node arrived: it can flip promotion on or off (one connection
                        // root ↔ two), which rewrites the whole top level. Recompute, don't append.
                        if (RootNodesChanged())
                        {
                            RefreshViewRootsPreservingState();
                            break;
                        }

                        if (args.NewItems != null)
                        {
                            foreach (var item in args.NewItems.OfType<ConnectionInfo>())
                            {
                                if (item.Parent != null)
                                    RefreshObject(item.Parent);
                                else
                                    AddObject(item);
                            }
                        }
                        break;
                    case NotifyCollectionChangedAction.Move:
                        if (args.NewItems != null)
                        {
                            var topLevelMoved = false;
                            foreach (var item in args.NewItems.OfType<ConnectionInfo>())
                            {
                                if (item.Parent != null)
                                    RefreshObject(item.Parent);
                                else
                                    topLevelMoved = true;
                            }

                            // Reordering the view roots is something RefreshObject can't express —
                            // rebuild the top level, keeping state.
                            if (topLevelMoved)
                                RefreshViewRootsPreservingState();
                        }
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        // Losing a root node can promote the survivor (two connection roots down
                        // to one), so the remaining root's children move to the top level.
                        if (RootNodesChanged())
                            RefreshViewRootsPreservingState();
                        else
                            RemoveObjects(args.OldItems);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        if (_connectionTreeModel != null)
                            SetObjects(ComputeViewRoots(_connectionTreeModel));
                        break;
                }
            }
            else
            {
                RefreshObject(sender);

                if (sender is ConnectionInfo connectionInfo)
                {
                    var parent = connectionInfo.Parent;
                    while (parent != null)
                    {
                        RefreshObject(parent);
                        parent = parent.Parent;
                    }
                }
            }

            AutoResizeColumn(Columns[0]);

            // turn filtering back on
            if (!filteringEnabled) return;
            ModelFilter = filter;
            UpdateFiltering();
        }
        finally
        {
            EndUpdate();
        }
    }

    protected override void UpdateFiltering()
    {
        base.UpdateFiltering();
        if (_columnAutoResizeSuspended)
            return;
        if (Columns.Count > 0)
            AutoResizeColumn(Columns[0]);
    }

    private void TvConnections_AfterSelect(object sender, EventArgs e)
    {
        try
        {
            var nodes = GetSelectedNodes();

            // When RDP ActiveX steals focus, ObjectListView may clear selection
            // or select the wrong node (due to auto-scroll). Use the click target
            // captured in WM_MOUSEACTIVATE before the scroll happened (#68).
            if (_pendingClickTarget != null)
            {
                var target = _pendingClickTarget;
                _pendingClickTarget = null;

                if (nodes.Count == 0 || (nodes.Count == 1 && nodes[0] != target))
                {
                    DevLog.Write($"Correcting selection → {target.Name} (was {nodes.FirstOrDefault()?.Name ?? "empty"})");
                    SelectObject(target);
                    EnsureModelVisible(target);
                    nodes = [target];
                }
            }
            else
            {
                _pendingClickTarget = null;
            }

            DevLog.Write($"SelectedNodes={nodes.Count}, First={nodes.FirstOrDefault()?.Name}");
            _slowClickRenameHandler?.CancelIfDifferentNode(nodes.FirstOrDefault());
            AppWindows.ConfigForm.SelectedTreeNodes = nodes;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("tvConnections_AfterSelect (UI.Window.ConnectionTreeWindow) failed", ex);
        }
    }

    private ConnectionInfo? _pendingClickTarget;

    private void OnMouse_Down(object sender, MouseEventArgs e)
    {
        DevLog.Write($"at ({e.X},{e.Y}) Button={e.Button} Focused={Focused} SelectedObject={SelectedObject}");
        if (!Focused)
            Focus();

        // _pendingClickTarget may already be set by WM_MOUSEACTIVATE (before
        // the tree scrolled). Only override if not already set (#68).
        if (_pendingClickTarget == null)
        {
            var hit = OlvHitTest(e.X, e.Y);
            _pendingClickTarget = hit.Item?.RowObject as ConnectionInfo;
        }
        DevLog.Write($"pendingClickTarget={_pendingClickTarget?.Name}");
    }

    private void OnMouse_DoubleClick(object sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.Clicks < 2) return;
        // ReSharper disable once NotAccessedVariable
        var listItem = GetItemAt(mouseEventArgs.X, mouseEventArgs.Y, out _);
        if (listItem?.RowObject is not ConnectionInfo clickedNode) return;
        _slowClickRenameHandler?.Cancel();
        DoubleClickHandler.Execute(clickedNode);
    }

    private void OnMouse_SingleClick(object sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.Clicks > 1) return;
        if (mouseEventArgs.Button != MouseButtons.Left) return;
        // ReSharper disable once NotAccessedVariable
        var listItem = GetItemAt(mouseEventArgs.X, mouseEventArgs.Y, out _);
        if (listItem?.RowObject is not ConnectionInfo clickedNode) return;
        _slowClickRenameHandler?.Execute(clickedNode);
        SingleClickHandler.Execute(clickedNode);
    }

    private void OnMouse_MiddleClick(object sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.Button != MouseButtons.Middle) return;
        var listItem = GetItemAt(mouseEventArgs.X, mouseEventArgs.Y, out _);
        if (listItem?.RowObject is not ConnectionInfo clickedNode) return;
        MiddleClickHandler.Execute(clickedNode);
    }

    private void TvConnections_CellToolTipShowing(object sender, ToolTipShowingEventArgs e)
    {
        try
        {
            // Name column: when the (content-sized, capped) name is ellipsized, surface the full
            // text. This is independent of the description-tooltip preference below.
            if (e.ColumnIndex == 0 && e.Item != null && e.Model is ConnectionInfo)
            {
                var fullName = ((OLVColumn)Columns[0]).GetStringValue(e.Model);
                if (IsNameTruncated(e.Item, fullName))
                {
                    e.Text = fullName;
                    return;
                }
            }

            if (!Properties.OptionsAppearancePage.Default.ShowDescriptionTooltipsInTree)
            {
                // setting text to null prevents the tooltip from being shown
                e.Text = null;
                return;
            }

            var nodeProducingTooltip = (ConnectionInfo)e.Model;
            var description = nodeProducingTooltip.Description;
            var tags = nodeProducingTooltip.EnvironmentTags ?? "";
            if (!string.IsNullOrWhiteSpace(tags))
            {
                e.Text = string.IsNullOrWhiteSpace(description)
                    ? $"Tags: {tags}"
                    : $"{description}\nTags: {tags}";
            }
            else
            {
                e.Text = description;
            }
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(
                "tvConnections_MouseMove (UI.Window.ConnectionTreeWindow) failed",
                ex);
        }
    }

    /// <summary>
    /// Estimates whether the Name cell's text is wider than the space available for it, so the
    /// caller knows a full-text tooltip is warranted. Uses the item's content offset (indent +
    /// glyph, mirroring the tree renderer) plus the node icon.
    /// </summary>
    private bool IsNameTruncated(OLVListItem item, string text)
    {
        if (string.IsNullOrEmpty(text) || Columns.Count == 0)
            return false;

        var indent = item.Position.X;
        const int padding = 6;
        var available = Columns[0].Width - indent - SmallImageSize.Width - padding;

        // No room for text at all — a deep node in a narrow pane. Nothing of a non-empty name
        // is legible, which is exactly when the tooltip matters most.
        if (available <= 0)
            return true;

        return TextRenderer.MeasureText(text, Font).Width > available;
    }

    private void OnBeforeLabelEdit(object sender, LabelEditEventArgs e)
    {
        if (_nodeInEditMode || sender is not ConnectionTree)
            return;

        if (IsReadOnly || !_allowEdit || SelectedNode is PuttySessionInfo || SelectedNode is RootPuttySessionsNodeInfo)
        {
            e.CancelEdit = true;
            return;
        }

        _nodeInEditMode = true;
        _contextMenu.DisableShortcutKeys();
    }

    private void ConnectionTree_FormatCell(object sender, FormatCellEventArgs e)
    {
        if (e.Model is not ConnectionInfo connectionInfo)
            return;

        var colorString = connectionInfo.Color;
        if (string.IsNullOrEmpty(colorString))
            return;

        try
        {
            System.Drawing.ColorConverter converter = new();
            var converted = converter.ConvertFromString(colorString);
            if (converted is System.Drawing.Color color)
                e.SubItem.ForeColor = color;
        }
        catch
        {
            // If color parsing fails, just ignore and use default color
        }
    }

    private void OnAfterLabelEdit(object sender, LabelEditEventArgs e)
    {
        if (!_nodeInEditMode)
            return;

        try
        {
            _contextMenu.EnableShortcutKeys();
            mRemoteNG.Tree.ConnectionTreeModel.RenameNode(SelectedNode, e.Label ?? string.Empty);
            _nodeInEditMode = false;
            _allowEdit = false;
            _slowClickRenameHandler?.Cancel();
            // ensures that if we are filtering and a new item is added that doesn't match the filter, it will be filtered out
            _connectionTreeSearchTextFilter.SpecialInclusionList.Clear();
            UpdateFiltering();
            AppWindows.ConfigForm.SelectedTreeNode = SelectedNode;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("tvConnections_AfterLabelEdit (UI.Window.ConnectionTreeWindow) failed", ex);
        }
    }

    #endregion
}