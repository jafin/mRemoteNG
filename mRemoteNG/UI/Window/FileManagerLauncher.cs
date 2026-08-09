using System;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.Messages;
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

            ConnectionWindow? host = Runtime.WindowList?.OfType<ConnectionWindow>().FirstOrDefault();
            if (host is null)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                    "Open a connection panel before opening the file manager.");
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