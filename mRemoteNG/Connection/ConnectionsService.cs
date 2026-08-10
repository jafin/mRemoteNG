using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Config;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Connections.Multiuser;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Putty;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.Window;

namespace mRemoteNG.Connection;

[SupportedOSPlatform("windows")]
public class ConnectionsService(PuttySessionsManager puttySessionsManager)
{
    private static readonly Lock SaveLock = new();
    private static readonly CompositeFormat ConnectionFileAlreadyOpenFormat = CompositeFormat.Parse("Connection file '{0}' is already open.");
    private static readonly CompositeFormat ConnectionsNotSavedFormat = CompositeFormat.Parse("Your changes were not saved: {0}");
    private const string NothingLoadedReason = "no connection file is loaded.";
    private const string NoConnectionsReason = "there are no connections to save.";
    private const string PendingSaveTimedOutMessage =
        "Your last change could not be written: a save that was already running did not finish in time.";
    private readonly PuttySessionsManager _puttySessionsManager = puttySessionsManager ?? throw new ArgumentNullException(nameof(puttySessionsManager));
    private readonly IDataProvider<string> _localConnectionPropertiesDataProvider = new FileDataProvider(Path.Combine(SettingsFileInfo.SettingsPath, SettingsFileInfo.LocalConnectionProperties));
    private readonly LocalConnectionPropertiesXmlSerializer _localConnectionPropertiesSerializer = new LocalConnectionPropertiesXmlSerializer();
    private int _saveBatchDepth;
    private bool _saveRequested;
    private bool _saveAsyncRequested;
    private System.Threading.Timer? _saveDebounceTimer;
    private volatile bool _savePending;
    private string _pendingPropertyNameTrigger = "";
    private const int SaveDebounceMs = 2000;

    /// <summary>
    /// How long <see cref="FlushPendingSaves()"/> waits for a save that is already running.
    /// </summary>
    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(20);

    private bool BatchingSaves => _saveBatchDepth > 0;
    // Cached SQL custom encryption password — avoids re-prompting on every reload (#1646)
    private SecureString? _cachedSqlEncryptionPassword;

    public bool IsConnectionsFileLoaded { get; set; }
    public bool UsingDatabase { get; private set; }
    public string? ConnectionFileName { get; private set; }
    public RemoteConnectionsSyncronizer? RemoteConnectionsSyncronizer { get; set; }
    public DateTime LastSqlUpdate { get; set; }
    public DateTime LastFileUpdate { get; set; }

    public ConnectionTreeModel? ConnectionTreeModel { get; private set; }

    public void NewConnectionsFile(string filename)
    {
        try
        {
            filename.ThrowIfNullOrEmpty(nameof(filename));
            ConnectionTreeModel newConnectionsModel = new();
            newConnectionsModel.AddRootNode(new RootNodeInfo(RootNodeType.Connection));
            SaveConnections(newConnectionsModel, false, new SaveFilter(), filename, true);
            LoadConnections(false, false, filename);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage(Language.CouldNotCreateNewConnectionsFile, ex);
        }
    }

    public static ConnectionInfo? CreateQuickConnect(string connectionString, ProtocolType protocol)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, Language.QuickConnectNoHostname);
                return null;
            }

            // Extract RDP-specific flags before parsing host/port.
            // Supported flags: -ra[:true|false]  (UseRestrictedAdmin)
            //                  -rcg[:true|false] (UseRemoteCredentialGuard)
            // Example: "myserver -ra:false -rcg:false"
            bool? rdpRestrictedAdminOverride = null;
            bool? rdpRcgOverride = null;

            if (connectionString.Contains(' '))
            {
                string[] parts = connectionString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                connectionString = parts[0];

                foreach (string part in parts.Skip(1))
                {
                    if (part.Equals("-ra", StringComparison.OrdinalIgnoreCase) ||
                        part.Equals("-ra:true", StringComparison.OrdinalIgnoreCase))
                        rdpRestrictedAdminOverride = true;
                    else if (part.Equals("-ra:false", StringComparison.OrdinalIgnoreCase))
                        rdpRestrictedAdminOverride = false;
                    else if (part.Equals("-rcg", StringComparison.OrdinalIgnoreCase) ||
                             part.Equals("-rcg:true", StringComparison.OrdinalIgnoreCase))
                        rdpRcgOverride = true;
                    else if (part.Equals("-rcg:false", StringComparison.OrdinalIgnoreCase))
                        rdpRcgOverride = false;
                }
            }

            UriBuilder uriBuilder = new()
            {
                Scheme = "dummyscheme"
            };
            string explicitUsername = string.Empty;

            if (connectionString.Contains('@'))
            {
                string[] x = connectionString.Split('@');
                explicitUsername = x[0];
                connectionString = x[1];
            }
            if (connectionString.Contains(':'))
            {
                string[] x = connectionString.Split(':');
                connectionString = x[0];
                uriBuilder.Port = Convert.ToInt32(x[1], CultureInfo.InvariantCulture);
            }

            uriBuilder.Host = connectionString;

            ConnectionInfo newConnectionInfo = new();
            newConnectionInfo.CopyFrom(DefaultConnectionInfo.Instance);

            newConnectionInfo.Name = Properties.OptionsTabsPanelsPage.Default.IdentifyQuickConnectTabs
                ? string.Format(CultureInfo.InvariantCulture, Language.Quick, connectionString)
                : connectionString;

            newConnectionInfo.Protocol = protocol;
            newConnectionInfo.Hostname = connectionString;
            if (!string.IsNullOrWhiteSpace(explicitUsername))
            {
                newConnectionInfo.Username = explicitUsername;
            }

            if (uriBuilder.Port == -1)
            {
                newConnectionInfo.SetDefaultPort();
            }
            else
            {
                newConnectionInfo.Port = uriBuilder.Port;
            }

            if (string.IsNullOrEmpty(newConnectionInfo.Panel))
            {
                // Use the currently active panel instead of hardcoding "General" (#1682)
                if (FrmMain.IsCreated && FrmMain.Default.pnlDock.ActiveDocument is ConnectionWindow activeCw)
                    newConnectionInfo.Panel = activeCw.TabText;
                else
                    newConnectionInfo.Panel = "General";
            }

            newConnectionInfo.IsQuickConnect = true;

            // Apply RDP-specific flag overrides (only meaningful for RDP protocol)
            if (protocol == ProtocolType.RDP)
            {
                if (rdpRestrictedAdminOverride.HasValue)
                    newConnectionInfo.UseRestrictedAdmin = rdpRestrictedAdminOverride.Value;
                if (rdpRcgOverride.HasValue)
                    newConnectionInfo.UseRCG = rdpRcgOverride.Value;
            }

            return newConnectionInfo;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage(Language.QuickConnectFailed, ex);
            return null;
        }
    }

    public void LoadAdditionalConnectionFile(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return;

        try
        {
            // Prevent opening the same file twice (#2331)
            if (ConnectionTreeModel != null &&
                ConnectionTreeModel.RootNodes.OfType<RootNodeInfo>()
                    .Any(r => string.Equals(r.Filename, filename, StringComparison.OrdinalIgnoreCase)))
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                    string.Format(CultureInfo.InvariantCulture, ConnectionFileAlreadyOpenFormat, filename));
                return;
            }

            IConnectionsLoader connectionLoader = new XmlConnectionsLoader(filename);
            ConnectionTreeModel? loadedModel = connectionLoader.Load();

            if (loadedModel == null) return;

            if (ConnectionTreeModel == null)
            {
                LoadConnections(false, false, filename);
            }
            else
            {
                foreach (ContainerInfo root in loadedModel.RootNodes)
                {
                    if (root is RootNodeInfo rni && string.IsNullOrEmpty(rni.Filename))
                    {
                        rni.Filename = filename;
                    }
                    ConnectionTreeModel.AddRootNode(root);
                }
            }

            PersistAdditionalFileList();
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage(string.Format(CultureInfo.InvariantCulture, Language.LoadFromXmlFailed, filename), ex);
        }
    }

    public void CloseAdditionalConnectionFile(RootNodeInfo rootNode)
    {
        if (ConnectionTreeModel == null || rootNode == null) return;
        if (ConnectionTreeModel.RootNodes.Count <= 1) return;

        // Don't allow closing the primary connection file
        if (string.Equals(rootNode.Filename, ConnectionFileName, StringComparison.OrdinalIgnoreCase))
            return;

        ConnectionTreeModel.RemoveRootNode(rootNode);
        PersistAdditionalFileList();
    }

    public void LoadAdditionalConnectionFiles()
    {
        string saved = Properties.OptionsConnectionsPage.Default.AdditionalConnectionFiles;
        if (string.IsNullOrWhiteSpace(saved)) return;

        string[] files = saved.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (string file in files)
        {
            string expanded = Environment.ExpandEnvironmentVariables(file.Trim());
            if (File.Exists(expanded))
            {
                LoadAdditionalConnectionFile(expanded);
            }
        }
    }

    private void PersistAdditionalFileList()
    {
        if (ConnectionTreeModel == null)
        {
            Properties.OptionsConnectionsPage.Default.AdditionalConnectionFiles = "";
            Properties.OptionsConnectionsPage.Default.Save();
            return;
        }

        var additionalFiles = ConnectionTreeModel.RootNodes
            .OfType<RootNodeInfo>()
            .Where(r => !string.IsNullOrEmpty(r.Filename) &&
                        !string.Equals(r.Filename, ConnectionFileName, StringComparison.OrdinalIgnoreCase) &&
                        r.Type == RootNodeType.Connection)
            .Select(r => r.Filename)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Properties.OptionsConnectionsPage.Default.AdditionalConnectionFiles = string.Join("|", additionalFiles);
        Properties.OptionsConnectionsPage.Default.Save();
    }

    /// <summary>
    /// Load connections from a source. <see cref="connectionFileName"/> is ignored if
    /// <see cref="useDatabase"/> is true.
    /// </summary>
    /// <param name="useDatabase"></param>
    /// <param name="import"></param>
    /// <param name="connectionFileName"></param>
    public void LoadConnections(bool useDatabase, bool import, string connectionFileName)
    {
        ConnectionTreeModel? oldConnectionTreeModel = ConnectionTreeModel;
        bool oldIsUsingDatabaseValue = UsingDatabase;

        IConnectionsLoader connectionLoader;
        if (useDatabase)
        {
            IDatabaseConnector dbConnector = DatabaseConnectorFactory.DatabaseConnectorFromSettings();
            SqlDataProvider sqlDataProvider = new(dbConnector);
            SqlDatabaseMetaDataRetriever metaDataRetriever = new();
            SqlDatabaseVersionVerifier versionVerifier = new(dbConnector);
            bool triedCached = false;
            connectionLoader = new SqlConnectionsLoader(
                _localConnectionPropertiesSerializer,
                _localConnectionPropertiesDataProvider,
                dbConnector,
                sqlDataProvider,
                metaDataRetriever,
                versionVerifier,
                new LegacyRijndaelCryptographyProvider(),
                (filename) =>
                {
                    // Return cached password on first call (avoids re-prompting on every reload — #1646)
                    if (_cachedSqlEncryptionPassword != null && !triedCached)
                    {
                        triedCached = true;
                        return new Optional<SecureString>(_cachedSqlEncryptionPassword);
                    }
                    // Cached password was wrong or not set — clear cache and prompt
                    _cachedSqlEncryptionPassword?.Dispose();
                    _cachedSqlEncryptionPassword = null;
                    Optional<SecureString> result = MiscTools.PasswordDialog(filename, false);
                    if (result.Any())
                        _cachedSqlEncryptionPassword = result.First();
                    return result;
                });
        }
        else
        {
            connectionLoader = new XmlConnectionsLoader(connectionFileName);
        }

        ConnectionTreeModel newConnectionTreeModel = null!;
        try
        {
            newConnectionTreeModel = connectionLoader.Load();
            if (useDatabase)
            {
                LastSqlUpdate = DateTime.Now.ToUniversalTime();
                TrySaveSqlConnectionsCache(newConnectionTreeModel);
            }
        }
        catch (Exception ex) when (useDatabase)
        {
            string cachePath = Path.Combine(SettingsFileInfo.SettingsPath, SettingsFileInfo.SqlConnectionsCache);
            if (File.Exists(cachePath))
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                    $"Could not load connections from database ({ex.Message}). Loading from local cache in read-only mode.");
                connectionLoader = new XmlConnectionsLoader(cachePath);
                newConnectionTreeModel = connectionLoader.Load();
            }
            else
            {
                throw;
            }
        }

        if (newConnectionTreeModel == null)
        {
            DialogFactory.ShowLoadConnectionsFailedDialog(connectionFileName, "Decrypting connection file failed", IsConnectionsFileLoaded);
            return;
        }

        IsConnectionsFileLoaded = true;
        ConnectionFileName = connectionFileName;
        Properties.OptionsConnectionsPage.Default.ConnectionFilePath = connectionFileName;
        Properties.OptionsConnectionsPage.Default.Save();

        UsingDatabase = useDatabase;

        if (!import)
        {
            _puttySessionsManager.AddSessions();
            newConnectionTreeModel.RootNodes.AddRange(_puttySessionsManager.RootPuttySessionsNodes);
        }

        // Set Filename on root nodes if not set
        if (!useDatabase)
        {
            foreach (var root in newConnectionTreeModel.RootNodes.OfType<RootNodeInfo>())
            {
                if (string.IsNullOrEmpty(root.Filename)) root.Filename = connectionFileName;
            }
        }

        ConnectionTreeModel = newConnectionTreeModel;
        UpdateCustomConsPathSetting(connectionFileName);
        RaiseConnectionsLoadedEvent(oldConnectionTreeModel is not null ? new Optional<ConnectionTreeModel>(oldConnectionTreeModel) : new Optional<ConnectionTreeModel>(), newConnectionTreeModel, oldIsUsingDatabaseValue, useDatabase, connectionFileName);
        Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, $"Connections loaded using {connectionLoader.GetType().Name}");
    }

    /// <summary>
    /// When turned on, calls to <see cref="SaveConnections()"/> or
    /// <see cref="SaveConnectionsAsync"/> will not immediately execute.
    /// Instead, they will be deferred until <see cref="EndBatchingSaves"/>
    /// is called.
    /// </summary>
    public void BeginBatchingSaves()
    {
        _saveBatchDepth++;
    }

    /// <summary>
    /// Immediately executes a single <see cref="SaveConnections()"/> or
    /// <see cref="SaveConnectionsAsync"/> if one has been requested
    /// since calling <see cref="BeginBatchingSaves"/>.
    /// </summary>
    public void EndBatchingSaves()
    {
        if (_saveBatchDepth > 0)
            _saveBatchDepth--;

        // A nested context ending does not end the outer one. Dispatching here would write
        // early; worse, dropping the depth to zero would let the outer context's own edits
        // through undeferred.
        if (_saveBatchDepth > 0)
            return;

        // Take and clear the requests together. Left set, they made the *next* batch end with
        // a save nobody asked for; left unread, they would drop the one that was asked for.
        bool asyncRequested = _saveAsyncRequested;
        bool requested = _saveRequested;
        _saveAsyncRequested = false;
        _saveRequested = false;

        if (asyncRequested)
            SaveConnectionsAsync();
        else if (requested)
            SaveConnections();
    }

    /// <summary>
    /// All calls to <see cref="SaveConnections()"/> or <see cref="SaveConnectionsAsync"/>
    /// will be deferred until the returned <see cref="DisposableAction"/> is disposed.
    /// Once disposed, this will immediately executes a single <see cref="SaveConnections()"/>
    /// or <see cref="SaveConnectionsAsync"/> if one has been requested.
    /// Place this call in a 'using' block to represent a batched saving context.
    /// </summary>
    /// <returns></returns>
    public DisposableAction BatchedSavingContext()
    {
        return new DisposableAction(BeginBatchingSaves, EndBatchingSaves);
    }

    /// <summary>
    /// Saves the currently loaded <see cref="ConnectionTreeModel"/> with
    /// no <see cref="SaveFilter"/>.
    /// </summary>
    public void SaveConnections()
    {
        if (ConnectionTreeModel is null || ConnectionFileName is null)
        {
            ReportSaveNotPerformed(NothingLoadedReason);
            return;
        }

        SaveConnections(ConnectionTreeModel, UsingDatabase, new SaveFilter(), ConnectionFileName);
    }

    /// <summary>
    /// Saves immediately, superseding any debounced save that has not run yet.
    /// </summary>
    /// <remarks>
    /// For explicit user instructions such as File &gt; Save Connections, where deferring the
    /// write by two seconds is wrong and leaving the timer armed afterwards would write the
    /// same state a second time.
    /// </remarks>
    public void SaveConnectionsNow()
    {
        lock (SaveLock)
        {
            _savePending = false;
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
            _pendingPropertyNameTrigger = "";
            SaveConnections();
        }
    }

    /// <summary>
    /// Reports a save that did not happen. The user is told through the notification panel
    /// rather than the log alone: a change that silently fails to reach disk is
    /// indistinguishable from one that was stored, which is how a master password came to be
    /// believed set while the file kept the old one.
    /// </summary>
    private static void ReportSaveNotPerformed(string reason)
    {
        Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
            string.Format(CultureInfo.InvariantCulture, ConnectionsNotSavedFormat, reason));
    }

    /// <summary>
    /// Saves the given <see cref="ConnectionTreeModel"/>.
    /// If <see cref="useDatabase"/> is true, <see cref="connectionFileName"/> is ignored
    /// </summary>
    /// <param name="connectionTreeModel"></param>
    /// <param name="useDatabase"></param>
    /// <param name="saveFilter"></param>
    /// <param name="connectionFileName"></param>
    /// <param name="forceSave">Bypasses safety checks that prevent saving if a connection file isn't loaded.</param>
    /// <param name="propertyNameTrigger">
    /// Optional. The name of the property that triggered
    /// this save.
    /// </param>
    public void SaveConnections(ConnectionTreeModel connectionTreeModel, bool useDatabase, SaveFilter saveFilter, string connectionFileName, bool forceSave = false, string propertyNameTrigger = "")
    {
        if (connectionTreeModel == null)
        {
            ReportSaveNotPerformed(NoConnectionsReason);
            return;
        }

        if (!forceSave && !IsConnectionsFileLoaded)
        {
            ReportSaveNotPerformed(NothingLoadedReason);
            return;
        }

        // Not a failure: the request is deferred to EndBatchingSaves, which drains it.
        if (BatchingSaves)
        {
            _saveRequested = true;
            return;
        }

        try
        {
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, "Saving connections...");
            RemoteConnectionsSyncronizer?.Disable();

            bool previouslyUsingDatabase = UsingDatabase;

            if (useDatabase)
            {
                ISaver<ConnectionTreeModel> saver = (ISaver<ConnectionTreeModel>)new SqlConnectionsSaver(saveFilter, _localConnectionPropertiesSerializer, _localConnectionPropertiesDataProvider);
                saver.Save(connectionTreeModel, propertyNameTrigger);
                LastSqlUpdate = DateTime.Now.ToUniversalTime();
            }
            else
            {
                // XML Saving with support for multiple roots/files
                foreach (var rootNode in connectionTreeModel.RootNodes.OfType<RootNodeInfo>())
                {
                    // PuTTY sessions are read-only (imported from registry) — never save them
                    // to disk. Without this check, PuTTY root (which has an empty Filename)
                    // would overwrite the main connections file with only PuTTY data.
                    if (rootNode.Type == Tree.Root.RootNodeType.PuttySessions)
                        continue;

                    string targetFile = rootNode.Filename;
                    if (string.IsNullOrEmpty(targetFile)) targetFile = connectionFileName;

                    // If Save As is detected (connectionFileName arg != ConnectionFileName prop), 
                    // and this is the "main" root (checked by Filename matching ConnectionFileName or being empty),
                    // then redirect to the new connectionFileName.
                    if (connectionFileName != ConnectionFileName && (rootNode.Filename == ConnectionFileName || string.IsNullOrEmpty(rootNode.Filename)))
                    {
                        targetFile = connectionFileName;
                        // Optionally update the root's filename to the new one?
                        // rootNode.Filename = connectionFileName; // Side effect?
                    }

                    var tempModel = new ConnectionTreeModel();
                    tempModel.AddRootNode(rootNode);

                    ISaver<ConnectionTreeModel> saver = new XmlConnectionsSaver(targetFile, saveFilter);
                    saver.Save(tempModel, propertyNameTrigger);

                    if (targetFile == connectionFileName && File.Exists(connectionFileName))
                        LastFileUpdate = File.GetLastWriteTimeUtc(connectionFileName);
                }
            }

            UsingDatabase = useDatabase;
            ConnectionFileName = connectionFileName;
            RaiseConnectionsSavedEvent(connectionTreeModel, previouslyUsingDatabase, UsingDatabase, connectionFileName);
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, "Successfully saved connections");
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector?.AddExceptionMessage(string.Format(CultureInfo.InvariantCulture, Language.ConnectionsFileCouldNotSaveAs, connectionFileName), ex, logOnly: false);
        }
        finally
        {
            RemoteConnectionsSyncronizer?.Enable();
        }
    }

    /// <summary>
    /// Save the currently loaded connections asynchronously
    /// </summary>
    /// <param name="propertyNameTrigger">
    /// Optional. The name of the property that triggered
    /// this save.
    /// </param>
    public void SaveConnectionsAsync(string propertyNameTrigger = "")
    {
        if (BatchingSaves)
        {
            _saveAsyncRequested = true;
            return;
        }

        // Debounce: reset the timer on each call so that rapid-fire PropertyChanged
        // events (e.g. from HostStatusMonitor or bulk edits) coalesce into a single
        // save instead of queuing N independent saves — each of which re-encrypts
        // every password with PBKDF2 at 600K iterations. See issue #83.
        //
        // The timer runs on the thread pool and keeps nothing alive, so the write is only
        // guaranteed to happen because FlushPendingSaves completes it on the way out.
        lock (SaveLock)
        {
            _pendingPropertyNameTrigger = propertyNameTrigger;
            _savePending = true;
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new System.Threading.Timer(_ => WritePendingSave(), null, SaveDebounceMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Writes a save that <see cref="SaveConnectionsAsync"/> scheduled but has not yet
    /// performed, and returns once there is nothing outstanding.
    /// </summary>
    /// <remarks>
    /// Call this before the process exits. Without it, an edit made inside the debounce
    /// window is discarded when the thread pool goes away with the process.
    /// </remarks>
    /// <returns>False if a save already in progress did not finish within the time limit.</returns>
    public bool FlushPendingSaves() => FlushPendingSaves(DefaultFlushTimeout);

    /// <inheritdoc cref="FlushPendingSaves()"/>
    public bool FlushPendingSaves(TimeSpan timeout)
    {
        if (!_savePending)
            return true;

        // The write runs on the calling thread deliberately. Handing it to a worker and
        // waiting would leave the UI thread in a blocking wait while the worker marshals its
        // progress messages to the notification panel — a Control.Invoke deadlock, and one
        // that would happen on every exit rather than rarely. What can genuinely block here
        // instead is a debounced save already in flight, so that is what is time-boxed.
        if (!SaveLock.TryEnter(timeout))
        {
            Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg, PendingSaveTimedOutMessage);
            return false;
        }

        try
        {
            WritePendingSaveUnderLock();
        }
        finally
        {
            SaveLock.Exit();
        }

        return true;
    }

    private void WritePendingSave()
    {
        lock (SaveLock)
            WritePendingSaveUnderLock();
    }

    private void WritePendingSaveUnderLock()
    {
        // Whoever gets here first performs the write; the debounce timer and the flush race
        // by design, and the loser must not write a second time.
        if (!_savePending)
            return;

        _savePending = false;
        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;

        string propertyNameTrigger = _pendingPropertyNameTrigger;
        _pendingPropertyNameTrigger = "";

        ConnectionTreeModel? treeModel = ConnectionTreeModel;
        string? fileName = ConnectionFileName;
        if (treeModel is null || fileName is null)
        {
            ReportSaveNotPerformed(NothingLoadedReason);
            return;
        }

        SaveConnections(treeModel, UsingDatabase, new SaveFilter(), fileName, propertyNameTrigger: propertyNameTrigger);
    }

    public static string GetStartupConnectionFileName() =>
        GetStartupConnectionFileName(UI.Forms.FrmChooseConnectionsFile.Prompt);

    internal static string GetStartupConnectionFileName(
        Func<IReadOnlyList<ConnectionsFileResolver.Candidate>,
            ConnectionsFileResolver.Candidate?,
            (ConnectionsFileResolver.Candidate? Choice, bool RememberChoice)> promptFactory)
    {
        // Command-line /cons: or /c: override (session-only, not persisted)
        if (!string.IsNullOrWhiteSpace(Tools.Cmdline.StartupArgumentsInterpreter.CustomConnectionFile))
        {
            return Tools.Cmdline.StartupArgumentsInterpreter.CustomConnectionFile;
        }

        // Always enumerate candidates before deciding, even when a
        // ConnectionFilePath is saved in Options. The saved path is treated as
        // one candidate among others: if it is still the only one that exists
        // we return it silently; if newer files exist in other well-known
        // locations we offer the picker. Short-circuiting on the saved path
        // meant users who once picked a path would never be told about a newer
        // file showing up alongside it, which is the regression #95 reported.
        IReadOnlyList<ConnectionsFileResolver.Candidate> candidates = ConnectionsFileResolver.DiscoverCandidates();
        bool userCancelled = false;
        if (candidates.Count > 0)
        {
            ConnectionsFileResolver.Candidate? chosen = ConnectionsFileResolver.Resolve(candidates, promptFactory);
            if (chosen is not null) return chosen.Path;
            // Null after a real prompt = user clicked Cancel. Treat that as
            // "don't auto-load anything from a remembered path" — skip the
            // saved-path fallback below and use the edition default. Without
            // this skip, Cancel would silently load whatever is in
            // ConnectionFilePath, which is exactly the "Cancel still runs the
            // previous session" behaviour the user reported.
            userCancelled = true;
        }

        // No candidate files on disk (or user explicitly cancelled the picker)
        // — fall back to the saved path only when the user did NOT cancel
        // (create-on-first-save path for fresh boxes with a custom setting),
        // and only then to the edition default.
        if (!userCancelled &&
            !string.IsNullOrWhiteSpace(Properties.OptionsConnectionsPage.Default.ConnectionFilePath))
        {
            return Environment.ExpandEnvironmentVariables(Properties.OptionsConnectionsPage.Default.ConnectionFilePath);
        }

        return GetDefaultStartupConnectionFileName();
    }

    public static string GetDefaultStartupConnectionFileName()
    {
        return Runtime.IsPortableEdition ? GetDefaultStartupConnectionFileNamePortableEdition() : GetDefaultStartupConnectionFileNameNormalEdition();
    }

    private static void UpdateCustomConsPathSetting(string filename)
    {
        if (filename == GetDefaultStartupConnectionFileName())
        {
            Properties.OptionsBackupPage.Default.LoadConsFromCustomLocation = false;
        }
        else
        {
            Properties.OptionsBackupPage.Default.LoadConsFromCustomLocation = true;
            Properties.OptionsBackupPage.Default.BackupLocation = filename;
        }
    }

    private static string GetDefaultStartupConnectionFileNameNormalEdition()
    {
        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Application.ProductName ?? "mRemoteNG", ConnectionsFileInfo.DefaultConnectionsFile);
        return File.Exists(appDataPath) ? appDataPath : GetDefaultStartupConnectionFileNamePortableEdition();
    }

    private static string GetDefaultStartupConnectionFileNamePortableEdition()
    {
        return Path.Combine(ConnectionsFileInfo.DefaultConnectionsPath, ConnectionsFileInfo.DefaultConnectionsFile);
    }

    private static void TrySaveSqlConnectionsCache(ConnectionTreeModel connectionTreeModel)
    {
        try
        {
            string cachePath = Path.Combine(SettingsFileInfo.SettingsPath, SettingsFileInfo.SqlConnectionsCache);
            ConnectionTreeModel cacheModel = new();
            foreach (RootNodeInfo root in connectionTreeModel.RootNodes.OfType<RootNodeInfo>())
                cacheModel.AddRootNode(root);
            XmlConnectionsSaver cacheSaver = new(cachePath, new SaveFilter());
            cacheSaver.Save(cacheModel);
            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, $"SQL connections cache saved to '{cachePath}'");
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("Failed to save SQL connections cache", ex);
        }
    }

    #region Events

    public event EventHandler<ConnectionsLoadedEventArgs>? ConnectionsLoaded;
    public event EventHandler<ConnectionsSavedEventArgs>? ConnectionsSaved;

    private void RaiseConnectionsLoadedEvent(Optional<ConnectionTreeModel> previousTreeModel, ConnectionTreeModel newTreeModel, bool previousSourceWasDatabase, bool newSourceIsDatabase, string newSourcePath)
    {
        ConnectionsLoaded?.Invoke(this, new ConnectionsLoadedEventArgs(previousTreeModel, newTreeModel, previousSourceWasDatabase, newSourceIsDatabase, newSourcePath));
    }

    private void RaiseConnectionsSavedEvent(ConnectionTreeModel modelThatWasSaved, bool previouslyUsingDatabase, bool usingDatabase, string connectionFileName)
    {
        ConnectionsSaved?.Invoke(this, new ConnectionsSavedEventArgs(modelThatWasSaved, previouslyUsingDatabase, usingDatabase, connectionFileName));
    }

    #endregion
}