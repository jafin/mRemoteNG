using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Config;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Properties;
using mRemoteNG.Tree;
using mRemoteNG.UI.TaskDialog;
using WeifenLuo.WinFormsUI.Docking;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.UI.Tabs
{
    [SupportedOSPlatform("windows")]
    public partial class ConnectionTab : DockContent
    {
        /// <summary>
        ///Silent close ignores the popup asking for confirmation
        /// </summary>
        public bool silentClose { get; set; }

        /// <summary>
        /// Protocol close ignores the interface controller cleanup and the user confirmation dialog
        /// </summary>
        public bool protocolClose { get; set; }

        /// <summary>
        /// Disconnect-only close (tab context menu "Disconnect"). The tab is kept open
        /// showing the reconnect panel when KeepTabsOpenAfterDisconnect is enabled.
        /// Closing the tab itself always removes the tab, regardless of that option.
        /// </summary>
        public bool disconnectOnly { get; set; }

        public ConnectionInfo? TrackedConnectionInfo { get; private set; }

        private Label? _closedStateLabel;
        private Panel? _closedStatePanel;

        private readonly SplitContainer _sessionSplit;

        public ConnectionTab()
        {
            InitializeComponent();

            // Created here, before any session exists, and never rebuilt. A protocol reparents its
            // native window onto InterfaceControl's handle, and moving a managed control between
            // parents destroys and recreates that handle — which would orphan the native child. So
            // the session must be born inside its final parent. Collapsed, this is invisible and
            // Panel1 fills the tab exactly as the tab itself used to.
            _sessionSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel2Collapsed = true,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 6
            };

            Controls.Add(_sessionSplit);

            Font = ConnectionTabAppearanceSettings.GetTabFont(Font);
            GotFocus += ConnectionTab_GotFocus;
        }

        /// <summary>
        /// The control a session's <c>InterfaceControl</c> is parented into.
        /// </summary>
        /// <remarks>
        /// Not the tab itself any more. Everything that needs "the tab that owns this session"
        /// should go through <see cref="OwnerOf"/> rather than reading <c>Parent</c>.
        /// </remarks>
        public Control SessionHost => _sessionSplit.Panel1;

        /// <summary>Whether a side panel is currently shown beside the session.</summary>
        public bool IsSidePanelVisible => !_sessionSplit.Panel2Collapsed;

        /// <summary>
        /// Shows <paramref name="panel"/> beside the session, taking <paramref name="width"/> pixels.
        /// </summary>
        public void ShowSidePanel(Control panel, int width)
        {
            ArgumentNullException.ThrowIfNull(panel);

            if (!_sessionSplit.Panel2.Contains(panel))
            {
                _sessionSplit.Panel2.Controls.Clear();
                panel.Dock = DockStyle.Fill;
                _sessionSplit.Panel2.Controls.Add(panel);
            }

            _sessionSplit.Panel2Collapsed = false;
            ApplySidePanelWidth(width);
            NotifySessionOfResize();
        }

        /// <summary>Hides the side panel, giving the space back to the session.</summary>
        public void HideSidePanel()
        {
            if (_sessionSplit.Panel2Collapsed)
                return;

            _sessionSplit.Panel2Collapsed = true;
            NotifySessionOfResize();
        }

        /// <summary>The width currently given to the side panel, or 0 when it is hidden.</summary>
        public int SidePanelWidth =>
            _sessionSplit.Panel2Collapsed ? 0 : _sessionSplit.Width - _sessionSplit.SplitterDistance - _sessionSplit.SplitterWidth;

        private void ApplySidePanelWidth(int width)
        {
            int available = _sessionSplit.Width - _sessionSplit.SplitterWidth;
            if (available <= 0)
                return;

            // SplitterDistance throws if it falls outside the panels' minimum sizes, and the tab can
            // be narrower than the requested panel width.
            int distance = available - Math.Max(width, _sessionSplit.Panel2MinSize);
            distance = Math.Clamp(distance, _sessionSplit.Panel1MinSize, available - _sessionSplit.Panel2MinSize);

            if (distance >= _sessionSplit.Panel1MinSize && distance <= available - _sessionSplit.Panel2MinSize)
                _sessionSplit.SplitterDistance = distance;
        }

        /// <summary>
        /// Tells the hosted protocol its area changed.
        /// </summary>
        /// <remarks>
        /// Protocols listen to the <i>tab's</i> resize, and collapsing or expanding the split does
        /// not resize the tab — only <see cref="SessionHost"/>. Without this the session's native
        /// window keeps its old size and is clipped by the panel that just appeared.
        /// </remarks>
        private void NotifySessionOfResize()
        {
            foreach (Control child in _sessionSplit.Panel1.Controls)
            {
                if (child is InterfaceControl { IsDisposed: false } interfaceControl)
                    interfaceControl.Protocol?.NotifyHostResized();
            }
        }

        /// <summary>
        /// Walks up from <paramref name="control"/> to the tab that owns it.
        /// </summary>
        /// <remarks>
        /// A session's <c>InterfaceControl</c> is no longer a direct child of the tab, so
        /// <c>Parent is ConnectionTab</c> no longer answers this question.
        /// </remarks>
        public static ConnectionTab? OwnerOf(Control? control)
        {
            for (Control? candidate = control; candidate is not null; candidate = candidate.Parent)
            {
                if (candidate is ConnectionTab tab)
                    return tab;
            }

            return null;
        }

        private void ConnectionTab_GotFocus(object sender, EventArgs e)
        {
            TabHelper.Instance.CurrentTab = this;
        }

        public void TrackConnection(ConnectionInfo connectionInfo)
        {
            TrackedConnectionInfo = connectionInfo;
        }

        private bool _hasUnreadActivity;
        public bool HasUnreadActivity
        {
            get => _hasUnreadActivity;
            set
            {
                if (_hasUnreadActivity == value) return;
                _hasUnreadActivity = value;
                DockHandler?.Pane?.Refresh();
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            HasUnreadActivity = false;
        }

        public void ShowClosedState()
        {
            HideClosedState();

            ConnectionInfo? info = TrackedConnectionInfo;
            if (info == null)
            {
                // Fallback: simple label when no connection info is available
                _closedStateLabel ??= new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _closedStateLabel.Text = Language.ConnenctionCloseEvent;
                Controls.Add(_closedStateLabel);
                _closedStateLabel.BringToFront();
                return;
            }

            _closedStatePanel = BuildClosedStatePanel(info);
            Controls.Add(_closedStatePanel);
            _closedStatePanel.BringToFront();
        }

        public void HideClosedState()
        {
            if (_closedStateLabel != null && Controls.Contains(_closedStateLabel))
                Controls.Remove(_closedStateLabel);

            if (_closedStatePanel != null)
            {
                if (Controls.Contains(_closedStatePanel))
                    Controls.Remove(_closedStatePanel);
                _closedStatePanel.Dispose();
                _closedStatePanel = null;
            }
        }

        private Panel BuildClosedStatePanel(ConnectionInfo info)
        {
            Panel outer = new() { Dock = DockStyle.Fill };

            // Detect dark background to pick readable text color
            Color bg = BackColor;
            bool isDark = bg.GetBrightness() < 0.4f;
            Color fg = isDark ? Color.White : SystemColors.ControlText;
            Color fgDim = isDark ? Color.FromArgb(160, 160, 160) : SystemColors.GrayText;

            Label lblName = new()
            {
                Text = info.Name,
                Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.None,
                ForeColor = fg,
                BackColor = Color.Transparent,
            };

            string details = $"{info.Protocol}   {info.Hostname}:{info.Port}";
            if (!string.IsNullOrWhiteSpace(info.Description))
                details += $"\n{info.Description}";

            Label lblDetails = new()
            {
                Text = details,
                Font = new Font(Font.FontFamily, 9.5f),
                AutoSize = true,
                ForeColor = fgDim,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.None,
            };

            Button btnConnect = new()
            {
                Text = Language.Connect,
                AutoSize = true,
                Padding = new Padding(24, 4, 24, 4),
                Anchor = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                ForeColor = fg,
                BackColor = isDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control,
            };
            btnConnect.Click += (_, _) => Runtime.ConnectionInitiator.OpenConnection(info);

            // TableLayoutPanel with Anchor=None centers each control horizontally
            TableLayoutPanel table = new()
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 0, 0),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(lblName, 0, 0);
            table.Controls.Add(lblDetails, 0, 1);
            table.Controls.Add(btnConnect, 0, 2);

            outer.Controls.Add(table);

            // Center the table block both horizontally and vertically in the outer panel
            void CenterTable(object? s, EventArgs a)
            {
                if (outer.Width == 0 || outer.Height == 0) return;
                Size preferred = table.PreferredSize;
                table.Location = new Point(
                    Math.Max(0, (outer.Width - preferred.Width) / 2),
                    Math.Max(10, (outer.Height - preferred.Height) / 2));
            }

            outer.SizeChanged += CenterTable;
            return outer;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!protocolClose)
            {
                // If the tab is showing the closed/disconnected state (no active protocol),
                // skip protocol close and confirmation — there's nothing to disconnect.
                bool hasActiveProtocol = Tag is InterfaceControl ic && ic.Protocol != null && !ic.IsDisposed;

                if (!hasActiveProtocol)
                {
                    // Tab is in closed/disconnected state — no protocol to shut down.
                    // Mark protocolClose so downstream logic skips protocol cleanup.
                    protocolClose = true;
                    HideClosedState();
                }
                else if (!silentClose)
                {
                    if (Settings.Default.ConfirmCloseConnection == (int)ConfirmCloseEnum.All)
                    {
                        DialogResult result = CTaskDialog.MessageBox(this, GeneralAppInfo.ProductName,
                                                            string
                                                                .Format(CultureInfo.CurrentCulture, Language.ConfirmCloseConnectionPanelMainInstruction,
                                                                        TabText), "", "", "",
                                                            Language.CheckboxDoNotShowThisMessageAgain,
                                                            ETaskDialogButtons.DisconnectCancel, ESysIcons.Question,
                                                            ESysIcons.Question);
                        if (CTaskDialog.VerificationChecked)
                        {
                            Settings.Default.ConfirmCloseConnection = (int)ConfirmCloseEnum.Multiple;
                            Settings.Default.Save();
                        }

                        if (result == DialogResult.No)
                        {
                            e.Cancel = true;
                        }
                        else
                        {
                            CloseProtocolSafe();
                            if (KeepTabOpenAfterDisconnect)
                                e.Cancel = true;
                        }
                    }
                    else
                    {
                        CloseProtocolSafe();
                        if (KeepTabOpenAfterDisconnect)
                            e.Cancel = true;
                    }
                }
                else if (hasActiveProtocol)
                {
                    // silentClose = true → app shutdown or panel close. Just disconnect, don't keep tab.
                    CloseProtocolSafe();
                }
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// The protocol close handler (HandleProtocolClosed) shows the closed state with a
        /// Connect button (#61), so the form close is cancelled to keep the tab alive. This
        /// only applies to a disconnect request - closing the tab must close the tab.
        /// </summary>
        private bool KeepTabOpenAfterDisconnect =>
            disconnectOnly && Properties.OptionsTabsPanelsPage.Default.KeepTabsOpenAfterDisconnect;

        private void CloseProtocolSafe()
        {
            try
            {
                (Tag as InterfaceControl)?.Protocol?.Close();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector?.AddExceptionMessage("Error closing protocol", ex);
            }
        }


        #region HelperFunctions  

        public void RefreshInterfaceController()
        {
            try
            {
                InterfaceControl? interfaceControl = Tag as InterfaceControl;
                if (interfaceControl?.Info.Protocol == ProtocolType.VNC)
                    ((ProtocolVNC)interfaceControl.Protocol).RefreshScreen();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("RefreshIC (UI.Window.Connection) failed", ex);
            }
        }

        public void FireResizeEnd()
        {
            OnResizeEnd(EventArgs.Empty);
        }

        #endregion
    }
}