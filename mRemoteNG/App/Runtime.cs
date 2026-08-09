using System;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using mRemoteNG.App.Info;
using mRemoteNG.Config;
using mRemoteNG.Config.Connections.Multiuser;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Credential;
using mRemoteNG.Credential.Repositories;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.TaskDialog;

namespace mRemoteNG.App;

[SupportedOSPlatform("windows")]
public static class Runtime
{
    /// <summary>
    /// True when <c>verbose.log.enable</c> exists next to the exe.
    /// Enables verbose logging and build ID in title bar.
    /// </summary>
    public static bool IsDevMode => DevLog.IsEnabled;

    public static bool IsPortableEdition
    {
        get
        {
#if PORTABLE
            return true;
#else
                return false;
#endif
        }
    }

    /// <summary>
    /// Feature flag to enable the credential manager feature
    /// </summary>
    public static bool UseCredentialManager => false;

    public static WindowList WindowList { get; set; } = null!;

    public static MessageCollector MessageCollector { get; } = new();

    public static NotificationAreaIcon? NotificationAreaIcon { get; set; }

    public static ExternalToolsService ExternalToolsService { get; } = new();

    public static CommandSnippetsService CommandSnippetsService { get; } = new();

    public static ConnectionPresetService ConnectionPresetService { get; } = new();

    public static SecureString EncryptionKey { get; set; } =
        new RootNodeInfo(RootNodeType.Connection).PasswordString.ConvertToSecureString();

    public static ICredentialRepositoryList CredentialProviderCatalog { get; } = new CredentialRepositoryList();

    public static ConnectionInitiator ConnectionInitiator { get; set; } = new();

    public static ConnectionsService ConnectionsService { get; } = new(PuttySessionsManager.Instance);

    public static Container.DynamicFolderManager DynamicFolderManager { get; } = new();

    public static RestApiService? RestApi { get; set; }

    public static HostStatusMonitor? HostStatusMonitor { get; private set; }

    #region Connections Loading/Saving

    public static void LoadConnectionsAsync()
    {
        Thread t = new(LoadConnectionsBGd);
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    private static void LoadConnectionsBGd()
    {
        LoadConnections();
    }

    /// <summary>
    /// Loads the connections, optionally showing a file selection dialog.
    /// </summary>
    /// <param name="withDialog">
    /// Should we show the file selection dialog to allow the user to select
    /// a connection file
    /// </param>
    public static void LoadConnections(bool withDialog = false)
    {
        var connectionFileName = "";

        try
        {
            // disable sql update checking while we are loading updates
            ConnectionsService.RemoteConnectionsSyncronizer?.Disable();

            if (!withDialog && Properties.OptionsDBsPage.Default.UseSQLServer &&
                Properties.OptionsDBsPage.Default.ShowDatabasePickerOnStartup)
            {
                using var frm = new FrmPickDatabase();
                frm.ShowDialog();
            }

            if (withDialog)
            {
                var loadDialog = DialogFactory.BuildLoadConnectionsDialog();
                DialogResult dlgResult;
                try
                {
                    dlgResult = loadDialog.ShowDialog();
                }
                catch (Exception dlgEx)
                {
                    // Vista-style file dialog can fail with COMException (0x80040111) when
                    // Windows high contrast theme is active (#1386). Log and bail out gracefully
                    // to prevent infinite recursion in the outer exception handler.
                    MessageCollector.AddExceptionMessage("Could not open the file selection dialog.", dlgEx,
                        MessageClass.WarningMsg);
                    return;
                }

                if (dlgResult != DialogResult.OK)
                    return;

                connectionFileName = loadDialog.FileName;
                Properties.OptionsDBsPage.Default.UseSQLServer = false;
                Properties.OptionsDBsPage.Default.Save();
            }
            else if (!Properties.OptionsDBsPage.Default.UseSQLServer)
            {
                connectionFileName = ConnectionsService.GetStartupConnectionFileName();
            }

            ConnectionsService.LoadConnections(Properties.OptionsDBsPage.Default.UseSQLServer, false,
                connectionFileName);

            if (Properties.OptionsDBsPage.Default.UseSQLServer)
            {
                ConnectionsService.LastSqlUpdate = DateTime.Now.ToUniversalTime();
            }
            else
            {
                ConnectionsService.LastFileUpdate = File.GetLastWriteTimeUtc(connectionFileName);
            }

            UpdateRemoteConnectionsSynchronizer(Properties.OptionsDBsPage.Default.UseSQLServer, connectionFileName);

            // Start or stop host status monitor based on setting
            StartHostStatusMonitorIfEnabled();

            // re-enable sql update checking after updates are loaded
            ConnectionsService.RemoteConnectionsSyncronizer?.Enable();
        }
        catch (Exception ex)
        {
            try
            {
                ProgramRoot.CloseSplash();
            }
            catch (TaskCanceledException)

            {
                _ = 0; // Intentionally empty
            }
            catch (OperationCanceledException)

            {
                _ = 0; // Intentionally empty
            }

            if (Properties.OptionsDBsPage.Default.UseSQLServer)
            {
                MessageCollector.AddExceptionMessage(Language.LoadFromSqlFailed, ex);
                var commandButtons = string.Join("|", Language._TryAgain, Language.CommandOpenConnectionFile,
                    Language.CommandStartWithEmptyConnections,
                    string.Format(CultureInfo.CurrentCulture, Language.CommandExitProgram, Application.ProductName));
                CTaskDialog.ShowCommandBox(Application.ProductName ?? GeneralAppInfo.ProductName,
                    Language.LoadFromSqlFailed, Language.LoadFromSqlFailedContent,
                    MiscTools.GetExceptionMessageRecursive(ex), "", "", commandButtons, false, ESysIcons.Error,
                    ESysIcons.Error);
                switch (CTaskDialog.CommandButtonResult)
                {
                    case 0:
                        LoadConnections(withDialog);
                        return;
                    case 1:
                        Properties.OptionsDBsPage.Default.UseSQLServer = false;
                        LoadConnections(true);
                        return;
                    case 2:
                        Properties.OptionsDBsPage.Default.UseSQLServer = false;
                        Properties.OptionsDBsPage.Default.Save();
                        ConnectionsService.NewConnectionsFile(ConnectionsService.GetStartupConnectionFileName());
                        return;
                    default:
                        Application.Exit();
                        return;
                }
            }

            if ((ex is FileNotFoundException || ex is IOException || ex is UnauthorizedAccessException ||
                 ex is XmlException) && !withDialog)
            {
                MessageCollector.AddExceptionMessage(
                    string.Format(CultureInfo.InvariantCulture, Language.ConnectionsFileCouldNotBeLoadedNew,
                        connectionFileName), ex,
                    MessageClass.InformationMsg);

                string[] commandButtons =
                {
                    Language.ConfigurationCreateNew,
                    Language.ConfigurationCustomPath,
                    Language.ConfigurationImportFile,
                    Language.Exit
                };

                var answered = false;
                while (!answered)
                {
                    try
                    {
                        CTaskDialog.ShowTaskDialogBox(GeneralAppInfo.ProductName, Language.ConnectionFileNotFound, "",
                            "", "", "", "", string.Join(" | ", commandButtons), ETaskDialogButtons.None,
                            ESysIcons.Question, ESysIcons.Question);

                        switch (CTaskDialog.CommandButtonResult)
                        {
                            case 0:
                                ConnectionsService.NewConnectionsFile(connectionFileName);
                                answered = true;
                                break;
                            case 1:
                                LoadConnections(true);
                                answered = true;
                                break;
                            case 2:
                                ConnectionsService.NewConnectionsFile(connectionFileName);
                                if (ConnectionsService.ConnectionTreeModel is not null)
                                    Import.ImportFromFile(ConnectionsService.ConnectionTreeModel.RootNodes[0]);
                                answered = true;
                                break;
                            case 3:
                                Application.Exit();
                                answered = true;
                                break;
                        }
                    }
                    catch (Exception exc)
                    {
                        MessageCollector.AddExceptionMessage(
                            string.Format(CultureInfo.InvariantCulture, Language.ConnectionsFileCouldNotBeLoadedNew,
                                connectionFileName), exc, MessageClass.InformationMsg);
                    }
                }

                return;
            }

            MessageCollector.AddExceptionStackTrace(
                string.Format(CultureInfo.InvariantCulture, Language.ConnectionsFileCouldNotBeLoaded,
                    connectionFileName), ex);
            if (connectionFileName != ConnectionsService.GetStartupConnectionFileName())
            {
                LoadConnections(withDialog);
            }
            else
            {
                MessageBox.Show(FrmMain.Default,
                    string.Format(CultureInfo.CurrentCulture, Language.ErrorStartupConnectionFileLoad,
                        Environment.NewLine, Application.ProductName, ConnectionsService.GetStartupConnectionFileName(),
                        MiscTools.GetExceptionMessageRecursive(ex)), @"Could not load startup file.",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }
    }

    private static void UpdateRemoteConnectionsSynchronizer(bool useSql, string connectionFileName)
    {
        ConnectionsService.RemoteConnectionsSyncronizer?.Dispose();
        ConnectionsService.RemoteConnectionsSyncronizer = null;

        if (useSql)
        {
            ConnectionsService.RemoteConnectionsSyncronizer =
                new RemoteConnectionsSyncronizer(new SqlConnectionsUpdateChecker());
        }
        else if (Properties.OptionsConnectionsPage.Default.WatchConnectionFile &&
                 !string.IsNullOrEmpty(connectionFileName))
        {
            try
            {
                ConnectionsService.RemoteConnectionsSyncronizer =
                    new RemoteConnectionsSyncronizer(new FileConnectionsUpdateChecker(connectionFileName));
            }
            catch (Exception ex)
            {
                MessageCollector.AddExceptionMessage(
                    "Could not set up file watcher for connection file. File watching is disabled.", ex,
                    MessageClass.WarningMsg);
            }
        }
    }

    #endregion

    #region Host Status Monitor

    public static void StartHostStatusMonitorIfEnabled()
    {
        var model = ConnectionsService.ConnectionTreeModel;
        if (Properties.OptionsConnectionsPage.Default.ShowHostStatus && model != null)
        {
            // Recreate monitor if the model was replaced (reload/team-sync)
            if (HostStatusMonitor != null && !ReferenceEquals(HostStatusMonitor.Model, model))
            {
                HostStatusMonitor.Dispose();
                HostStatusMonitor = null;
            }

            if (HostStatusMonitor == null)
            {
                HostStatusMonitor = new HostStatusMonitor(model);
            }

            HostStatusMonitor.Start();
        }
        else
        {
            HostStatusMonitor?.Stop();
        }
    }

    #endregion
}