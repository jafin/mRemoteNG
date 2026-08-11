using System;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace mRemoteNG.App;

[SupportedOSPlatform("windows")]
public class Logger
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>The active log file plus five rolled-over backups.</summary>
    /// <remarks>
    /// Serilog counts the file it is currently writing, so the limit is one higher than log4net's
    /// <c>maxSizeRollBackups</c> was for the same "1 main log + 5 backups" policy.
    /// </remarks>
    private const int MaxRetainedFiles = 6;

    /// <remarks>
    /// <c>{Level,-6:u}</c> rather than <c>{Level:u6}</c>: a width given inside the format specifier
    /// truncates, which turned Information and Warning into <c>INFORM</c> and <c>WARNIN</c> in the
    /// log. Alignment pads without truncating, which is what log4net's <c>%-6level</c> did.
    /// </remarks>
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss,fff} [{ThreadId}] {Level,-6:u}- {Message:lj}{NewLine}{Exception}";

    public static readonly Logger Instance = new();

    private readonly Lock _rebuildLock = new();

    /// <summary>
    /// Controls how much reaches the log, live.
    /// </summary>
    /// <remarks>
    /// A switch rather than a fixed <c>MinimumLevel</c> for two reasons: changing it takes effect
    /// without rebuilding the logger, and it survives the rebuild <see cref="SetLogPath"/> performs
    /// when the path changes — a fixed level would be re-read from configuration there and quietly
    /// revert whatever the user had selected.
    /// </remarks>
    private readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);

    public ILogger? Log { get; private set; }

    public static string DefaultLogPath => BuildLogFilePath();

    private Logger()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (string.IsNullOrEmpty(Properties.OptionsNotificationsPage.Default.LogFilePath))
        {
            Properties.OptionsNotificationsPage.Default.LogFilePath = BuildLogFilePath();
        }

        ApplyConfiguredLevel();
        SetLogPath(Properties.OptionsNotificationsPage.Default.LogToApplicationDirectory ? DefaultLogPath : Properties.OptionsNotificationsPage.Default.LogFilePath);
    }

    /// <summary>
    /// Applies the verbosity the user configured, and takes effect immediately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven by the existing "write debug messages to the log" option rather than a setting of its
    /// own. That checkbox already says what this controls, and it previously only decided whether
    /// the message collector forwarded debug messages — the thirty-odd direct <c>Log.Debug</c> calls
    /// elsewhere ignored it, because the minimum level was hardcoded to Verbose and nothing was ever
    /// filtered. Reading it here makes the option mean the same thing everywhere.
    /// </para>
    /// <para>
    /// It also means the default falls out of the setting's own default of false, so the log stops
    /// carrying every debug message from every subsystem on every run for every user, while staying
    /// one checkbox away for anyone diagnosing a fault.
    /// </para>
    /// </remarks>
    public void ApplyConfiguredLevel() =>
        WriteDebugMessages = Properties.OptionsNotificationsPage.Default.TextLogMessageWriterWriteDebugMsgs;

    /// <summary>Whether messages below information are written.</summary>
    public bool WriteDebugMessages
    {
        get => _levelSwitch.MinimumLevel <= LogEventLevel.Debug;
        set => _levelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    public void SetLogPath(string path)
    {
        lock (_rebuildLock)
        {
            ILogger? previous = Log;

            Log = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .Enrich.WithThreadId()
                .WriteTo.File(
                    path,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: MaxFileSizeBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: MaxRetainedFiles,
                    outputTemplate: OutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();

            // Disposed after the replacement is live so a message logged mid-switch still lands.
            // Safe even when the path is unchanged: the file sink opens lazily, on the first write.
            (previous as IDisposable)?.Dispose();
        }
    }

    private static string BuildLogFilePath()
    {
        string logFilePath = Runtime.IsPortableEdition ? GetLogPathPortableEdition() : GetLogPathNormalEdition();

        string? logFileName = Path.ChangeExtension(Application.ProductName, ".log");

        if (logFileName == null) return "mRemoteNG.log";

        string logFile = Path.Combine(logFilePath, logFileName);

        return logFile;
    }

    private static string GetLogPathNormalEdition()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Application.ProductName ?? "mRemoteNG");
    }

    private static string GetLogPathPortableEdition()
    {
        string startupPath = Application.StartupPath;
        if (IsDirectoryWritable(startupPath))
            return startupPath;
        // Fallback for read-only or WebDAV drives: write log to %LOCALAPPDATA%
        return GetLogPathNormalEdition();
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath)) return false;
        try
        {
            string testFile = Path.Combine(dirPath, Path.GetRandomFileName());
            using var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            return false;
        }
    }

}