using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Messages.MessageFilteringOptions;
using mRemoteNG.Messages.MessageWriters;
using mRemoteNG.Messages.WriterDecorators;
using NUnit.Framework;

namespace mRemoteNGTests.App;

/// <summary>
/// What actually lands on disk, written through the same writer stack the application builds in
/// <c>MessageCollectorSetup</c>, against the real <see cref="Logger"/> singleton pointed at a
/// temporary directory.
/// </summary>
[TestFixture]
public class LoggerFileOutputTests
{
    private static readonly Regex LogLine = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) \[(?<thread>\d+)\] (?<level>[A-Z]+) *- (?<message>.*)$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly string[] ExpectedLevels = ["DEBUG", "INFORMATION", "WARNING", "ERROR"];

    private static readonly string[] ExpectedMessages =
        ["a debug message", "an informational message", "a warning message", "an error message"];

    private string _directory = "";
    private string _logFile = "";
    private MessageTypeFilterDecorator _writer = null!;
    private mRemoteNG.Properties.OptionsNotificationsPage _settings = null!;
    private (bool Debug, bool Info, bool Warning, bool Error) _originalFilters;

    [SetUp]
    public void Setup()
    {
        _settings = mRemoteNG.Properties.OptionsNotificationsPage.Default;
        _originalFilters = (_settings.TextLogMessageWriterWriteDebugMsgs,
                            _settings.TextLogMessageWriterWriteInfoMsgs,
                            _settings.TextLogMessageWriterWriteWarningMsgs,
                            _settings.TextLogMessageWriterWriteErrorMsgs);

        _settings.TextLogMessageWriterWriteDebugMsgs = true;
        _settings.TextLogMessageWriterWriteInfoMsgs = true;
        _settings.TextLogMessageWriterWriteWarningMsgs = true;
        _settings.TextLogMessageWriterWriteErrorMsgs = true;
        Logger.Instance.ApplyConfiguredLevel();

        _directory = Path.Combine(Path.GetTempPath(), "mRemoteNGTests-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _logFile = Path.Combine(_directory, "mRemoteNG.log");
        Logger.Instance.SetLogPath(_logFile);

        _writer = new MessageTypeFilterDecorator(new LogMessageTypeFilteringOptions(),
                                                 new TextLogMessageWriter(Logger.Instance));
    }

    [TearDown]
    public void TearDown()
    {
        (_settings.TextLogMessageWriterWriteDebugMsgs,
         _settings.TextLogMessageWriterWriteInfoMsgs,
         _settings.TextLogMessageWriterWriteWarningMsgs,
         _settings.TextLogMessageWriterWriteErrorMsgs) = _originalFilters;
        Logger.Instance.ApplyConfiguredLevel();

        // Releases the temporary file so the directory can go.
        Logger.Instance.SetLogPath(Logger.DefaultLogPath);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    [Test]
    public void EverySeverityIsWrittenWithTimestampThreadIdLevelAndMessage()
    {
        Write(MessageClass.DebugMsg, "a debug message");
        Write(MessageClass.InformationMsg, "an informational message");
        Write(MessageClass.WarningMsg, "a warning message");
        Write(MessageClass.ErrorMsg, "an error message");

        List<Match> lines = ReadLines(_logFile).Select(line => LogLine.Match(line)).ToList();

        Assert.That(lines.Select(l => l.Success), Is.All.True, "every line matches the output template");
        Assert.Multiple(() =>
        {
            // {Level:u6} truncated these to INFORM and WARNIN.
            Assert.That(lines.Select(l => l.Groups["level"].Value), Is.EqualTo(ExpectedLevels),
                "level names are written in full");
            Assert.That(lines.Select(l => l.Groups["message"].Value), Is.EqualTo(ExpectedMessages));
            Assert.That(lines.Select(l => l.Groups["thread"].Value).Distinct(StringComparer.Ordinal).Single(),
                Is.EqualTo(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture)),
                "the thread id enricher reports the thread that logged");
        });
    }

    [Test]
    public void DisablingDebugMessagesKeepsThemOutOfTheLogFile()
    {
        _settings.TextLogMessageWriterWriteDebugMsgs = false;
        Logger.Instance.ApplyConfiguredLevel();

        Write(MessageClass.DebugMsg, "a debug message");
        Write(MessageClass.InformationMsg, "an informational message");

        List<string> lines = ReadLines(_logFile);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.Contain("an informational message"));
    }

    [Test]
    public void DisablingDebugMessagesAlsoSilencesDirectLoggerCalls()
    {
        _settings.TextLogMessageWriterWriteDebugMsgs = false;
        Logger.Instance.ApplyConfiguredLevel();

        // The options pages log straight to Logger.Instance.Log, bypassing the filter decorator.
        Logger.Instance.Log?.Debug("a direct debug message");
        Logger.Instance.Log?.Information("a direct informational message");

        List<string> lines = ReadLines(_logFile);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.Contain("a direct informational message"));
    }

    [Test]
    public void ChangingTheLogPathRedirectsSubsequentMessagesWithoutLosingAny()
    {
        string relocated = Path.Combine(_directory, "relocated", "mRemoteNG.log");
        Directory.CreateDirectory(Path.GetDirectoryName(relocated)!);

        Write(MessageClass.InformationMsg, "before the move");
        Logger.Instance.SetLogPath(relocated);
        Write(MessageClass.InformationMsg, "after the move");

        List<string> before = ReadLines(_logFile);
        List<string> after = ReadLines(relocated);

        Assert.Multiple(() =>
        {
            Assert.That(before, Has.Count.EqualTo(1), "the old file keeps what it had and takes nothing more");
            Assert.That(before[0], Does.Contain("before the move"));
            Assert.That(after, Has.Count.EqualTo(1), "nothing was dropped across the switch");
            Assert.That(after[0], Does.Contain("after the move"));
        });
    }

    [Test]
    public void ReopeningTheSamePathAppendsInsteadOfStartingANewFile()
    {
        Write(MessageClass.InformationMsg, "before reopening");
        // What a restart does: a fresh logger against the path already on disk.
        Logger.Instance.SetLogPath(_logFile);
        Write(MessageClass.InformationMsg, "after reopening");

        Assert.Multiple(() =>
        {
            // If each start took a new roll counter instead, five restarts would be enough for the
            // retention policy to delete the history a user was asked to send.
            Assert.That(Directory.GetFiles(_directory, "mRemoteNG_*.log"), Is.Empty, "no roll counter was burned");
            Assert.That(ReadLines(_logFile), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AFileHeldByAnotherInstanceIsLeftAloneAndLoggingRollsToTheNextFile()
    {
        string contended = Path.Combine(_directory, "contended.log");

        // Stands in for a second copy of mRemoteNG holding its own log open. This — not size — is
        // what leaves _001, _002 … beside a local build; each roll counter is one such collision.
        using (new FileStream(contended, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            Logger.Instance.SetLogPath(contended);
            Write(MessageClass.InformationMsg, "logged despite the collision");
        }

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(contended).Length, Is.Zero, "the holder's file is not written to");
            Assert.That(ReadLines(Path.Combine(_directory, "contended_001.log")),
                Has.Exactly(1).Contains("logged despite the collision"));
        });
    }

    [Test]
    public void TheLogRollsOnSizeAndRetainsTheActiveFilePlusFiveBackups()
    {
        // 10 MB per file, so this writes ~70 MB to a temp directory to get past six rolls.
        string padding = new('x', 64 * 1024);
        for (int i = 0; i < 7 * 10 * 16 + 16; i++)
            Write(MessageClass.InformationMsg, padding);

        string[] files = Directory.GetFiles(_directory, "mRemoteNG*.log").Order(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(files, Has.Length.EqualTo(6), "the active file plus five backups, as log4net kept");
            Assert.That(files.Select(f => new FileInfo(f).Length),
                // The sink stops writing once the limit is reached, so a file overshoots by at most
                // the line that was in flight.
                Is.All.LessThan(10 * 1024 * 1024 + padding.Length + 256),
                "each file rolled at the size limit");
            Assert.That(files, Has.None.EndWith(Path.DirectorySeparatorChar + "mRemoteNG.log"),
                "the oldest file was discarded once the retention limit was passed");
        });
    }

    private void Write(MessageClass messageClass, string text) => _writer.Write(new Message(messageClass, text));

    /// <summary>Reads a log file the sink still holds open.</summary>
    private static List<string> ReadLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
