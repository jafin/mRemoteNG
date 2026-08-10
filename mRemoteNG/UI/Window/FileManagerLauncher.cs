using System;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.Messages;
using mRemoteNG.UI.Panels;
using WeifenLuo.WinFormsUI.Docking;

namespace mRemoteNG.UI.Window;

/// <summary>
/// Opens the file manager for a connection, reusing the tab if one is already open.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileManagerLauncher
{
    public static FileManagerTab? Open(ConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        try
        {
            if (FindOpenTab(connectionInfo) is { } existing)
            {
                existing.Activate();
                return existing;
            }

            ConnectionWindow? host = FindHostPanel(connectionInfo) ?? CreateHostPanel(connectionInfo);
            if (host is null)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                    "Could not open a connection panel to host the file manager.");
                return null;
            }

            // Its own SFTP session, resolved from the connection the same way every other
            // SSH.NET-backed caller does. It does not touch a session tab for the same
            // connection: sharing a transport is not available.
            SftpSession session = SftpSession.ForConnection(connectionInfo);
            FileManagerTab tab = new(connectionInfo, session);

            tab.Show(host.GetDockPanel(), DockState.Document);
            return tab;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector?.AddExceptionStackTrace(
                "Could not open the file manager", ex, MessageClass.ErrorMsg, false);
            return null;
        }
    }

    /// <summary>
    /// The panel the connection would open into, if it is already open. Falls back to any open
    /// panel so the file manager does not create a second one beside a perfectly good host.
    /// </summary>
    private static ConnectionWindow? FindHostPanel(ConnectionInfo connectionInfo)
    {
        if (Runtime.WindowList is null)
            return null;

        return (Runtime.WindowList.FromString(PanelNameFor(connectionInfo)) as ConnectionWindow)
               ?? Runtime.WindowList.OfType<ConnectionWindow>().FirstOrDefault();
    }

    /// <summary>
    /// The file manager is reachable from the connection tree before any session has been opened,
    /// so it cannot assume a panel exists — it opens one the same way a connection does.
    /// </summary>
    private static ConnectionWindow? CreateHostPanel(ConnectionInfo connectionInfo) =>
        PanelAdder.AddPanel(PanelNameFor(connectionInfo), showImmediately: true);

    private static string PanelNameFor(ConnectionInfo connectionInfo) =>
        string.IsNullOrEmpty(connectionInfo.Panel) ? "New Panel" : connectionInfo.Panel;

    private static FileManagerTab? FindOpenTab(ConnectionInfo connectionInfo)
    {
        if (Runtime.WindowList is null)
            return null;

        return Runtime.WindowList
            .OfType<ConnectionWindow>()
            .Select(w => w.GetDockPanel())
            .Where(panel => panel is not null)
            .SelectMany(panel => panel!.Documents.OfType<FileManagerTab>())
            .FirstOrDefault(tab => tab.ConnectionInfo == connectionInfo && !tab.IsDisposed);
    }
}