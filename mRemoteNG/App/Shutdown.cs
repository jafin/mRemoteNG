using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Putty;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;
using mRemoteNG.UI.Controls;
using mRemoteNG.UI.Forms;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.App;

[SupportedOSPlatform("windows")]
public static class Shutdown
{
    public static void Quit()
    {
        FrmMain.Default.Close();
        ProgramRoot.CloseSingletonInstanceMutex();
    }

    public static void Cleanup(Control quickConnectToolStrip,
        ExternalToolsToolStrip externalToolsToolStrip,
        MultiSshToolStrip multiSshToolStrip,
        MenuStrip mainMenu,
        FrmMain frmMain)
    {
        try
        {
            StopRestApi();
            StopAutoStartedExternalTools();
            StopPuttySessionWatcher();
            DisposeNotificationAreaIcon();
            SaveConnections();
            SaveSettings(quickConnectToolStrip, externalToolsToolStrip, multiSshToolStrip, mainMenu, frmMain);
            UnregisterBrowsers();
            PluginManager.Instance.ShutdownPlugins();
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace(Language.SettingsCouldNotBeSavedOrTrayDispose, ex);
        }
    }

    private static void StopRestApi()
    {
        Runtime.RestApi?.Dispose();
        Runtime.RestApi = null;
    }

    private static void StopAutoStartedExternalTools()
    {
        foreach (var tool in Runtime.ExternalToolsService.ExternalTools)
        {
            if (tool.StopOnShutdown)
                tool.StopTrackedProcess();
        }
    }

    private static void StopPuttySessionWatcher()
    {
        PuttySessionsManager.Instance.StopWatcher();
    }

    private static void DisposeNotificationAreaIcon()
    {
        if (Runtime.NotificationAreaIcon != null && Runtime.NotificationAreaIcon.Disposed == false)
            Runtime.NotificationAreaIcon.Dispose();
    }

    private static void SaveConnections()
    {
        DateTime lastUpdate;
        DateTime updateDate;
        DateTime currentDate = DateTime.Now;

        // Whatever the user already changed goes to disk first, unconditionally. The setting
        // below governs how often to save on a schedule; it was never meant to decide whether
        // an edit the user has already made survives being closed.
        //
        // A flush that times out means a save is still running. Starting the scheduled one on
        // top of it would have two writers on the same file and the same ".tmp" path, so skip
        // it: the in-flight save is already writing, and the timeout has been reported.
        if (!Runtime.ConnectionsService.FlushPendingSaves())
            return;

        int frequency = Properties.OptionsBackupPage.Default.SaveConnectionsFrequency;

        // Unassigned is the shipped default, and the migration off it runs only in
        // File > Options > Connections. A profile that never opened that page would
        // otherwise fall through to "no save on exit" — so consult the legacy setting the
        // migration reads instead of treating a fresh install as "never".
        if (frequency == (int)ConnectionsBackupFrequencyEnum.Unassigned)
            frequency = Properties.OptionsBackupPage.Default.SaveConsOnExit
                ? (int)ConnectionsBackupFrequencyEnum.OnExit
                : (int)ConnectionsBackupFrequencyEnum.Never;

        if (frequency == (int)ConnectionsBackupFrequencyEnum.OnExit)
        {
            Runtime.ConnectionsService.SaveConnections();
            return;
        }
        lastUpdate = Runtime.ConnectionsService.UsingDatabase ? Runtime.ConnectionsService.LastSqlUpdate : Runtime.ConnectionsService.LastFileUpdate;

        switch (frequency)
        {
            case (int)ConnectionsBackupFrequencyEnum.Daily:
                updateDate = lastUpdate.AddDays(1);
                break;
            case (int)ConnectionsBackupFrequencyEnum.Weekly:
                updateDate = lastUpdate.AddDays(7);
                break;
            default:
                return;
        }

        if (currentDate >= updateDate)
        {
            Runtime.ConnectionsService.SaveConnections();
        }
    }

    private static void SaveSettings(Control quickConnectToolStrip,
        ExternalToolsToolStrip externalToolsToolStrip,
        MultiSshToolStrip multiSshToolStrip,
        MenuStrip mainMenu,
        FrmMain frmMain)
    {
        Config.Settings.SettingsSaver.SaveSettings(quickConnectToolStrip, externalToolsToolStrip, multiSshToolStrip,
            mainMenu, frmMain);
    }

    private static void UnregisterBrowsers()
    {
        IeBrowserEmulation.Unregister();
    }
}