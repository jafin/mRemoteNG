using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.UI.Forms;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class SaveConnectionsOnEdit
{
    private readonly ConnectionsService _connectionsService;

    public SaveConnectionsOnEdit(ConnectionsService connectionsService)
    {
        ArgumentNullException.ThrowIfNull(connectionsService);
        _connectionsService = connectionsService;
        connectionsService.ConnectionsLoaded += ConnectionsServiceOnConnectionsLoaded;
    }

    private void ConnectionsServiceOnConnectionsLoaded(object sender, ConnectionsLoadedEventArgs connectionsLoadedEventArgs)
    {
        connectionsLoadedEventArgs.NewConnectionTreeModel.CollectionChanged += ConnectionTreeModelOnCollectionChanged;
        connectionsLoadedEventArgs.NewConnectionTreeModel.PropertyChanged += ConnectionTreeModelOnPropertyChanged;

        foreach (Tree.ConnectionTreeModel oldTree in connectionsLoadedEventArgs.PreviousConnectionTreeModel)
        {
            oldTree.CollectionChanged -= ConnectionTreeModelOnCollectionChanged;
            oldTree.PropertyChanged -= ConnectionTreeModelOnPropertyChanged;
        }
    }

    private void ConnectionTreeModelOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        string property = propertyChangedEventArgs.PropertyName ?? "";

        // Skip runtime-only properties that are never persisted to the connections file.
        // Without this filter, the HostStatusMonitor fires PropertyChanged for every
        // connection on each scan cycle, each triggering a full save that re-encrypts
        // ALL passwords with PBKDF2 (600K iterations per field) — causing 100% CPU on
        // one thread for minutes. See issue #83.
        if (property is nameof(Connection.ConnectionInfo.HostReachabilityStatus)
            or nameof(Connection.ConnectionInfo.OpenConnections)
            or nameof(Connection.ConnectionInfo.IsQuickConnect)
            or nameof(Connection.ConnectionInfo.PleaseConnect))
        {
            return;
        }

        SaveConnectionOnEdit(property);
    }

    private void ConnectionTreeModelOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        SaveConnectionOnEdit();
    }

    private void SaveConnectionOnEdit(string propertyName = "")
    {
        //OBSOLETE: mRemoteNG.Settings.Default.SaveConnectionsAfterEveryEdit is obsolete and should be removed in a future release
        if (Properties.OptionsBackupPage.Default.SaveConnectionsAfterEveryEdit || (Properties.OptionsBackupPage.Default.SaveConnectionsFrequency == (int)ConnectionsBackupFrequencyEnum.OnEdit))
        {
            if (FrmMain.Default.IsClosing)
                return;

            _connectionsService.SaveConnectionsAsync(propertyName);
        }
    }
}