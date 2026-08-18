using System;
using System.Globalization;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Properties;
using mRemoteNG.Resources.Language;
using mRemoteNG.UI.Forms;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.Config.Connections.Multiuser;

[SupportedOSPlatform("windows")]
public class RemoteConnectionsSyncronizer : IConnectionsUpdateChecker
{
    private readonly System.Timers.Timer _updateTimer;
    private readonly IConnectionsUpdateChecker _updateChecker;
    private readonly System.Threading.Lock _timerLock = new();
    private bool _disposed;

    /// <summary>
    /// How a reload is performed. A seam, so that a reload which throws can be exercised without a
    /// SQL server and a master password somebody has changed underneath us.
    /// </summary>
    internal static Action<bool, string> Reload { get; set; } =
        (useDatabase, fileName) => Runtime.ConnectionsService.LoadConnections(useDatabase, false, fileName);

    public double TimerIntervalInMilliseconds
    {
        get { return _updateTimer.Interval; }
    }

    /// <summary>
    /// Whether the poll timer is running. Read by tests, which otherwise could not tell a refused
    /// reload that stopped asking from one that will put the same password box up every interval.
    /// </summary>
    internal bool IsPolling
    {
        get
        {
            lock (_timerLock)
            {
                return !_disposed && _updateTimer.Enabled;
            }
        }
    }

    /// <summary>
    /// Gets the UTC time of the last successful external sync, or null if no sync has occurred yet.
    /// </summary>
    public DateTime? LastExternalSync { get; private set; }

    /// <summary>
    /// Raised when connections have been reloaded due to an external change (file or database).
    /// </summary>
    public event EventHandler? ConnectionsReloadedExternally;

    public RemoteConnectionsSyncronizer(IConnectionsUpdateChecker updateChecker)
    {
        _updateChecker = updateChecker;
        double intervalMs = OptionsDBsPage.Default.SQLReloadInterval * 1000.0;
        _updateTimer = new System.Timers.Timer(intervalMs > 0 ? intervalMs : 30000.0);
        SetEventListeners();
    }

    private void SetEventListeners()
    {
        _updateChecker.UpdateCheckStarted += OnUpdateCheckStarted;
        _updateChecker.UpdateCheckFinished += OnUpdateCheckFinished;
        _updateChecker.ConnectionsUpdateAvailable += (_, args) => ConnectionsUpdateAvailable?.Invoke(this, args);
        _updateTimer.Elapsed += (sender, args) => _updateChecker.IsUpdateAvailableAsync();
        ConnectionsUpdateAvailable += Load;
    }

    private void Load(object sender, ConnectionsUpdateAvailableEventArgs args)
    {
        // Update checkers (SQL polling, file watcher) raise this event from
        // background threads. Marshal the reload onto the UI thread so the
        // tree/model stays single-threaded — otherwise concurrent Children
        // mutations trip enumeration in GetRecursiveChildList. Fixes #102.
        if (FrmMain.IsCreated && FrmMain.Default.IsHandleCreated && FrmMain.Default.InvokeRequired)
        {
            FrmMain.Default.BeginInvoke(new Action(() => Load(sender, args)));
            return;
        }

        try
        {
            if (args.DatabaseConnector != null)
            {
                Reload(true, "");
            }
            else
            {
                if (Runtime.ConnectionsService.ConnectionFileName != null)
                    Reload(false, Runtime.ConnectionsService.ConnectionFileName);
            }
        }
        catch (SqlAuthenticationRefusedException ex)
        {
            // A reload nobody asked for must not become a crash dialog. This arrives when the
            // database's master password has been changed by another administrator and the person
            // here declined the prompt or got it wrong: the load is refused rather than answered
            // from the local copy, and without this the refusal would escape a timer callback and
            // surface as an unhandled exception over whatever they were doing.
            //
            // Polling stops with it, and that is the point rather than a side effect. The update is
            // still pending, so the next tick would find it again and put the same password box in
            // front of them every interval. What they keep is the tree they already authenticated
            // for; reloading connections deliberately — the options page, or a restart — starts the
            // synchronizer again with a fresh chance to type the new password.
            Disable();
            Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, string.Format(
                CultureInfo.CurrentCulture, Language.WarningSqlSyncAuthenticationRefused, ex.Message));
            return;
        }

        args.Handled = true;

        LastExternalSync = DateTime.UtcNow;
        string source = args.DatabaseConnector != null ? "database" : "file";
        Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
            $"Connections reloaded from external {source} change (team sync)");
        ConnectionsReloadedExternally?.Invoke(this, EventArgs.Empty);
    }

    public void Enable()
    {
        lock (_timerLock)
        {
            if (!_disposed)
                _updateTimer.Start();
        }
    }

    public void Disable()
    {
        lock (_timerLock)
        {
            if (!_disposed)
                _updateTimer.Stop();
        }
    }

    public bool IsUpdateAvailable()
    {
        return _updateChecker.IsUpdateAvailable();
    }

    public void IsUpdateAvailableAsync()
    {
        _updateChecker.IsUpdateAvailableAsync();
    }


    private void OnUpdateCheckStarted(object sender, EventArgs eventArgs)
    {
        lock (_timerLock)
        {
            if (!_disposed)
                _updateTimer.Stop();
        }
        UpdateCheckStarted?.Invoke(this, eventArgs);
    }

    private void OnUpdateCheckFinished(object sender, ConnectionsUpdateCheckFinishedEventArgs eventArgs)
    {
        lock (_timerLock)
        {
            if (!_disposed)
                _updateTimer.Start();
        }
        UpdateCheckFinished?.Invoke(this, eventArgs);
    }

    public event EventHandler? UpdateCheckStarted;
    public event UpdateCheckFinishedEventHandler? UpdateCheckFinished;
    public event ConnectionsUpdateAvailableEventHandler? ConnectionsUpdateAvailable;


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool itIsSafeToAlsoFreeManagedObjects)
    {
        if (!itIsSafeToAlsoFreeManagedObjects) return;
        lock (_timerLock)
        {
            if (_disposed) return;
            _disposed = true;
            _updateTimer.Dispose();
        }
        _updateChecker.Dispose();
    }
}