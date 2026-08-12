using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using mRemoteNG.App;
using mRemoteNG.App.Initialization;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;
using mRemoteNG.Messages.MessageWriters;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using NUnit.Framework;

namespace mRemoteNGTests.App;

/// <summary>
/// Nothing password-bearing reaches the message collector or the log file, at any level.
/// </summary>
/// <remarks>
/// <para>
/// The log now has a level a user can raise deliberately. This fixture is what stops that from
/// quietly becoming a way to read credentials back out of an installation: it drives the one
/// path that holds a secret and reports something about it — resolve a connection's credential,
/// translate it for a backend that cannot use it, replay the diagnostics the way the protocols
/// do — with debug enabled, and asserts the secrets are absent from both channels.
/// </para>
/// <para>
/// The secret-bearing properties are found by reflection rather than listed, so a password
/// property added to <see cref="ConnectionInfo"/> later is covered without anyone remembering
/// to come back here.
/// </para>
/// <para>
/// What this does not cover, so nobody reads it as more than it is: only
/// <see cref="AbstractConnectionRecord.Password"/> actually travels the path exercised here.
/// The gateway and proxy passwords are filled and asserted on too, but they reach the log
/// through <c>RdpProtocol</c>, which needs the RDP ActiveX control and a message pump and is
/// therefore not driven from a unit test. Their assertions are a tripwire for a future leak
/// into <i>this</i> path, not coverage of theirs.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public class LogSecretExclusionTests
{
    /// <summary>Distinctive enough that a substring match cannot be a coincidence.</summary>
    private const string SentinelPrefix = "sentinel-secret-4d81b2";

    private string _directory = "";
    private string _logFile = "";
    private MessageCollector _collector = null!;
    private mRemoteNG.Properties.OptionsNotificationsPage _settings = null!;
    private (bool Debug, bool Info, bool Warning, bool Error) _originalFilters;
    private string _originalEmptyCredentials = null!;
    private string _originalLogFilePath = null!;

    [SetUp]
    public void Setup()
    {
        _settings = mRemoteNG.Properties.OptionsNotificationsPage.Default;
        _originalFilters = (_settings.TextLogMessageWriterWriteDebugMsgs,
                            _settings.TextLogMessageWriterWriteInfoMsgs,
                            _settings.TextLogMessageWriterWriteWarningMsgs,
                            _settings.TextLogMessageWriterWriteErrorMsgs);

        // The worst case for this fixture: everything the user can turn on, turned on.
        _settings.TextLogMessageWriterWriteDebugMsgs = true;
        _settings.TextLogMessageWriterWriteInfoMsgs = true;
        _settings.TextLogMessageWriterWriteWarningMsgs = true;
        _settings.TextLogMessageWriterWriteErrorMsgs = true;
        Logger.Instance.ApplyConfiguredLevel();

        _originalEmptyCredentials = mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials;
        mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = "noinfo";

        // The logger is process-wide, so restoring the default path rather than the one that was
        // actually configured would redirect any later fixture's output somewhere it did not ask for.
        _originalLogFilePath = _settings.LogFilePath;

        _directory = Path.Combine(Path.GetTempPath(), "mRemoteNGTests-secretlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _logFile = Path.Combine(_directory, "mRemoteNG.log");
        Logger.Instance.SetLogPath(_logFile);

        // The application's own wiring, not an approximation of it: whatever the collector
        // accepts is what the log file gets.
        _collector = new MessageCollector();
        List<IMessageWriter> writers = [new TextLogMessageWriter(Logger.Instance)];
        MessageCollectorSetup.SetupMessageCollector(_collector, writers);
    }

    [TearDown]
    public void TearDown()
    {
        (_settings.TextLogMessageWriterWriteDebugMsgs,
         _settings.TextLogMessageWriterWriteInfoMsgs,
         _settings.TextLogMessageWriterWriteWarningMsgs,
         _settings.TextLogMessageWriterWriteErrorMsgs) = _originalFilters;
        Logger.Instance.ApplyConfiguredLevel();

        mRemoteNG.Properties.OptionsCredentialsPage.Default.EmptyCredentials = _originalEmptyCredentials;

        // Releases the temporary file so the directory can go, and puts the logger back where it
        // was rather than where it defaults to.
        Logger.Instance.SetLogPath(string.IsNullOrEmpty(_originalLogFilePath)
            ? Logger.DefaultLogPath
            : _originalLogFilePath);
        _settings.LogFilePath = _originalLogFilePath;

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    [Test]
    public void NoPasswordBearingPropertyOfAConnectionReachesTheCollectorOrTheLog()
    {
        ConnectionInfo connection = ConnectionWithEverySecretFilled(out List<string> sentinels);

        Assert.That(sentinels, Is.Not.Empty,
            "reflection found no password-bearing property — the fixture would be asserting nothing");

        ResolveTranslateAndReport(connection);

        string collected = string.Join(Environment.NewLine, _collector.Messages.Select(m => m.Text));
        string logged = ReadLog();

        Assert.Multiple(() =>
        {
            Assert.That(logged, Is.Not.Empty, "the flow reported something, so absence below is meaningful");
            foreach (string sentinel in sentinels)
            {
                Assert.That(collected, Does.Not.Contain(sentinel), $"{sentinel} reached the message collector");
                Assert.That(logged, Does.Not.Contain(sentinel), $"{sentinel} reached the log file");
            }
        });
    }

    [Test]
    public void ADiagnosticNamesWhatItCouldNotUseWithoutQuotingItsValue()
    {
        ConnectionInfo connection = ConnectionWithEverySecretFilled(out List<string> sentinels);

        ResolveTranslateAndReport(connection);

        string logged = ReadLog();

        Assert.Multiple(() =>
        {
            // The point of the message: the user is told the password was dropped. A log that
            // said nothing would also pass the exclusion assertions and be useless.
            Assert.That(logged, Does.Contain("password").IgnoreCase,
                "the failure is reported in terms of what could not be used");
            Assert.That(sentinels, Has.None.Matches<string>(logged.Contains),
                "and reports it without reproducing the value");
        });
    }

    [Test]
    public void RaisingTheLevelToDebugAddsNoSecretToWhatTheLogHolds()
    {
        ConnectionInfo connection = ConnectionWithEverySecretFilled(out List<string> sentinels);

        _settings.TextLogMessageWriterWriteDebugMsgs = false;
        Logger.Instance.ApplyConfiguredLevel();
        ResolveTranslateAndReport(connection);
        string quiet = ReadLog();

        _settings.TextLogMessageWriterWriteDebugMsgs = true;
        Logger.Instance.ApplyConfiguredLevel();
        ResolveTranslateAndReport(connection);
        string verbose = ReadLog();

        Assert.Multiple(() =>
        {
            Assert.That(verbose, Has.Length.GreaterThan(quiet.Length),
                "the level was genuinely raised, so the comparison below is between two different logs");
            foreach (string sentinel in sentinels)
                Assert.That(verbose, Does.Not.Contain(sentinel),
                    $"{sentinel} appeared only once the level was raised — verbose is an extraction path");
        });
    }

    /// <summary>
    /// Resolves the connection's credential, translates it for the OpenSSH backend and replays
    /// the diagnostics the way <c>PuttyBase.ReplayCredentialDiagnostics</c> and
    /// <c>ProtocolOpenSSH</c> do. OpenSSH is the backend that cannot take a password, so it is
    /// the one guaranteed to have something to report about a connection that has one.
    /// </summary>
    private void ResolveTranslateAndReport(ConnectionInfo connection)
    {
        SshCredentialResolver resolver = new([], new NoKeyLocator());
        using ResolvedSshCredential credential =
            resolver.Resolve(connection, SshCredentialResolutionOptions.ForOpenSsh);

        OpenSshCredentialArguments arguments = OpenSshArgsAdapter.Translate(credential, connection.Hostname);

        foreach (SshCredentialDiagnostic diagnostic in credential.Diagnostics.Concat(arguments.Unsupported))
        {
            MessageClass messageClass = diagnostic.Severity == SshCredentialDiagnosticSeverity.Information
                ? MessageClass.InformationMsg
                : MessageClass.ErrorMsg;
            _collector.AddMessage(messageClass, diagnostic.Message);
        }

        // What a protocol writes about the credential it is about to use. The rendering is
        // ResolvedSshCredential.ToString(), whose contract is provenance only.
        _collector.AddMessage(MessageClass.DebugMsg, $"Resolved credential: {credential}");
        _collector.AddMessage(MessageClass.DebugMsg, $"Destination: {arguments.Destination} {arguments.IdentityArgument}");
    }

    /// <summary>
    /// A connection whose every writable string property carrying a password or passphrase holds
    /// its own sentinel, so a leak names the property that leaked.
    /// </summary>
    private static ConnectionInfo ConnectionWithEverySecretFilled(out List<string> sentinels)
    {
        ConnectionInfo connection = new()
        {
            Protocol = ProtocolType.SSH2,
            Hostname = "example-host",
            Username = "alice",
        };

        sentinels = [];
        foreach (PropertyInfo property in SecretProperties())
        {
            string sentinel = $"{SentinelPrefix}-{property.Name}";
            property.SetValue(connection, sentinel);
            sentinels.Add(sentinel);
        }

        return connection;
    }

    private static IEnumerable<PropertyInfo> SecretProperties() =>
        typeof(ConnectionInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Passphrase", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the log file the sink still holds open.</summary>
    private string ReadLog()
    {
        if (!File.Exists(_logFile))
            return string.Empty;

        using FileStream stream = new(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Discovery off, so the fixture does not depend on what is in the user's .ssh.</summary>
    private sealed class NoKeyLocator : IDefaultSshKeyLocator
    {
        public string? Locate(DefaultKeyDiscoveryMode mode) => null;
    }
}
