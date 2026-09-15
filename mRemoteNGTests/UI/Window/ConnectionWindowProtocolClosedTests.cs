using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.Config;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Properties;
using mRemoteNG.UI.Tabs;
using mRemoteNG.UI.Window;
using NUnit.Framework;
using WeifenLuo.WinFormsUI.Docking;

namespace mRemoteNGTests.UI.Window;

[NonParallelizable]
public class ConnectionWindowProtocolClosedTests
{
    [Test]
    public void AProtocolCloseThatArrivesWhileTheTabDisposesLeavesTheTabToFinish() => RunWithMessagePump(() =>
    {
        int previousConfirmCloseConnection = Settings.Default.ConfirmCloseConnection;
        bool previousKeepTabsOpenAfterDisconnect = OptionsTabsPanelsPage.Default.KeepTabsOpenAfterDisconnect;
        bool previousAlwaysShowPanelSelectionDlg = OptionsTabsPanelsPage.Default.AlwaysShowPanelSelectionDlg;

        using Form hostForm = new()
        {
            Width = 800,
            Height = 600,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-10000, -10000)
        };

        DockPanel hostDockPanel = new()
        {
            Dock = DockStyle.Fill,
            DocumentStyle = DocumentStyle.DockingWindow,
            Theme = new VS2015LightTheme()
        };
        hostForm.Controls.Add(hostDockPanel);

        try
        {
            Settings.Default.ConfirmCloseConnection = (int)ConfirmCloseEnum.Never;
            OptionsTabsPanelsPage.Default.KeepTabsOpenAfterDisconnect = true;
            OptionsTabsPanelsPage.Default.AlwaysShowPanelSelectionDlg = false;

            hostForm.Show();
            Application.DoEvents();
            Application.DoEvents();

            using ConnectionWindow connectionWindow = new(new DockContent(), "Protocol Closed Test");
            connectionWindow.Show(hostDockPanel, DockState.Document);
            Application.DoEvents();
            Application.DoEvents();

            ConnectionInfo target = NewConnection("Target", "127.0.0.1");
            ConnectionTab tab = AddTab(connectionWindow, target);

            // A second tab keeps the panel open, so it does not close itself with the last tab.
            AddTab(connectionWindow, NewConnection("Other", "127.0.0.2"));

            StubProtocol protocol = new();
            bool? sessionDisposedByClose = null;
            bool? sessionStillInTab = null;

            // Stands in for the RDP control's teardown pumping messages. It is added before the
            // session, so the tab disposes it while the session is still in place, and it
            // delivers the protocol close that was queued before the tab began disposing.
            DisposeProbe probe = new(() =>
            {
                connectionWindow.Prot_Event_Closed(protocol);
                sessionDisposedByClose = protocol.InterfaceControl.IsDisposed;
                sessionStillInTab = protocol.InterfaceControl.Parent == tab;
            });
            tab.Controls.Add(probe);

            InterfaceControl session = new(tab, protocol, target) { OriginalInfo = target };
            protocol.InterfaceControl = session;
            tab.Tag = session;

            tab.Close();
            Application.DoEvents();

            Assert.Multiple(() =>
            {
                Assert.That(sessionDisposedByClose, Is.False,
                    "Disposing the session here releases the RDP control the tab's Dispose is still using.");
                Assert.That(sessionStillInTab, Is.True,
                    "The close must not pull the session out of a tab that is disposing it.");
                Assert.That(tab.IsDisposed, Is.True, "The tab should be disposed.");
                Assert.That(GetConnectionTabs(connectionWindow), Does.Not.Contain(tab),
                    "The tab should be gone from the panel, not left on screen empty.");
            });
        }
        finally
        {
            Settings.Default.ConfirmCloseConnection = previousConfirmCloseConnection;
            OptionsTabsPanelsPage.Default.KeepTabsOpenAfterDisconnect = previousKeepTabsOpenAfterDisconnect;
            OptionsTabsPanelsPage.Default.AlwaysShowPanelSelectionDlg = previousAlwaysShowPanelSelectionDlg;
            hostForm.Close();
        }
    });

    private static ConnectionInfo NewConnection(string name, string hostname) => new()
    {
        Name = name,
        Hostname = hostname,
        Protocol = ProtocolType.RDP,
        Panel = "General"
    };

    private static ConnectionTab AddTab(ConnectionWindow connectionWindow, ConnectionInfo connectionInfo)
    {
        ConnectionTab? tab = connectionWindow.AddConnectionTab(connectionInfo);
        if (tab == null)
        {
            // The window's DockPanel may need further layout passes before it accepts a tab.
            Application.DoEvents();
            Application.DoEvents();
            tab = connectionWindow.AddConnectionTab(connectionInfo);
        }

        return tab ?? throw new AssertionException($"Failed to create the tab for {connectionInfo.Name}.");
    }

    private static ConnectionTab[] GetConnectionTabs(ConnectionWindow connectionWindow)
    {
        FieldInfo dockField = typeof(ConnectionWindow).GetField("connDock", BindingFlags.Instance | BindingFlags.NonPublic)
                              ?? throw new AssertionException("Failed to resolve ConnectionWindow.connDock field.");
        DockPanel dockPanel = dockField.GetValue(connectionWindow) as DockPanel
                              ?? throw new AssertionException("Failed to resolve connection dock panel.");

        return dockPanel.DocumentsToArray().OfType<ConnectionTab>().ToArray();
    }

    private static void RunWithMessagePump(Action testAction)
    {
        Exception? caught = null;
        Thread thread = new(() =>
        {
            using Form form = new()
            {
                Width = 400,
                Height = 300,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-10000, -10000)
            };

            form.Load += (_, _) =>
            {
                try
                {
                    testAction();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    form.Close();
                }
            };

            Application.Run(form);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            thread.Interrupt();
            Assert.Fail("Test timed out after 30 seconds (message pump deadlock)");
        }

        if (caught != null)
            throw caught;
    }

    private sealed class StubProtocol : ProtocolBase
    {
        public override bool Connect() => true;

        // The real Close queues the teardown on another thread; the probe delivers it instead.
        public override void Close()
        {
        }
    }

    /// <summary>
    /// Runs an action the first time it is disposed.
    /// </summary>
    private sealed class DisposeProbe(Action onDispose) : Control
    {
        private Action? _onDispose = onDispose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Action? onDisposeOnce = _onDispose;
                _onDispose = null;
                onDisposeOnce?.Invoke();
            }

            base.Dispose(disposing);
        }
    }
}
