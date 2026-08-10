using System;
using System.IO;
using System.Linq;
using mRemoteNG.App;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNGTests.Properties;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// Covers the guarantee that an edit the user has made reaches disk, and that one which does
/// not is reported. Both were absent: the debounced save ran on a thread-pool timer that
/// nothing flushed, and every path that declined to save returned silently.
/// </summary>
[NonParallelizable]
public class ConnectionSaveDurabilityTests
{
    private ConnectionsService _connectionsService = null!;
    private string _filePath = null!;
    private IDisposable _tempFile = null!;
    private int _savesPerformed;

    [SetUp]
    public void Setup()
    {
        _tempFile = FileTestHelpers.DisposableTempFile(out _filePath, ".xml");
        File.WriteAllText(_filePath, Resources.confCons_v2_6);

        _connectionsService = new ConnectionsService(PuttySessionsManager.Instance);
        _connectionsService.LoadConnections(useDatabase: false, import: false, connectionFileName: _filePath);

        _savesPerformed = 0;
        _connectionsService.ConnectionsSaved += (_, _) => _savesPerformed++;

        Runtime.MessageCollector.ClearMessages();
    }

    [TearDown]
    public void TearDown()
    {
        // Leave nothing armed: the debounce timer outlives the test otherwise and fires
        // against the deleted temp file while a later test is running.
        _connectionsService.FlushPendingSaves();

        // The read-only tests leave the file unwritable, and File.Copy carries that onto every
        // rolling backup taken from it, so clearing just the original is not enough to let the
        // temp directory be removed.
        string? directory = Path.GetDirectoryName(_filePath);
        if (directory != null && Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.SetAttributes(file, FileAttributes.Normal);
        }

        _tempFile.Dispose();
    }

    private static string[] WarningsAndErrors() =>
        Runtime.MessageCollector.Messages
            .Where(m => m.Class is MessageClass.WarningMsg or MessageClass.ErrorMsg)
            .Select(m => m.Text)
            .ToArray();

    #region Flushing a pending save

    [Test]
    public void FlushingWritesASaveTheDebounceHasNotRunYet()
    {
        DateTime before = File.GetLastWriteTimeUtc(_filePath);

        _connectionsService.SaveConnectionsAsync();
        Assert.That(_savesPerformed, Is.Zero, "the debounce should not have elapsed yet");

        Assert.That(_connectionsService.FlushPendingSaves(), Is.True);

        Assert.That(_savesPerformed, Is.EqualTo(1));
        Assert.That(File.GetLastWriteTimeUtc(_filePath), Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void FlushingWritesNothingWhenNoSaveIsPending()
    {
        Assert.That(_connectionsService.FlushPendingSaves(), Is.True);
        Assert.That(_savesPerformed, Is.Zero);
    }

    [Test]
    public void FlushingTwiceWritesOnce()
    {
        _connectionsService.SaveConnectionsAsync();

        _connectionsService.FlushPendingSaves();
        _connectionsService.FlushPendingSaves();

        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    #endregion

    #region Coalescing

    [Test]
    public void RapidSuccessiveChangesProduceOneWrite()
    {
        // The debounce exists because each save re-encrypts every password at 600,000
        // iterations. Flushing must complete the pending save, not replay each request.
        for (int i = 0; i < 25; i++)
            _connectionsService.SaveConnectionsAsync($"Property{i}");

        _connectionsService.FlushPendingSaves();

        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    [Test]
    public void AnExplicitSaveSupersedesThePendingOne()
    {
        _connectionsService.SaveConnectionsAsync();

        _connectionsService.SaveConnectionsNow();
        Assert.That(_savesPerformed, Is.EqualTo(1));

        // The superseded debounce must not fire a second, redundant write.
        _connectionsService.FlushPendingSaves();
        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    #endregion

    #region Reporting

    [Test]
    public void ASuccessfulSaveReportsNothing()
    {
        _connectionsService.SaveConnections();

        Assert.That(_savesPerformed, Is.EqualTo(1));
        Assert.That(WarningsAndErrors(), Is.Empty);
    }

    [Test]
    public void ASaveSkippedBecauseNothingIsLoadedIsReported()
    {
        ConnectionsService emptyService = new(PuttySessionsManager.Instance);

        emptyService.SaveConnections();

        Assert.That(WarningsAndErrors(), Is.Not.Empty,
            "a save that did not happen must not return silently");
    }

    [Test]
    public void AFailingSaveIsReported()
    {
        File.SetAttributes(_filePath, FileAttributes.ReadOnly);

        Assert.DoesNotThrow(() => _connectionsService.SaveConnections());

        Assert.That(WarningsAndErrors(), Is.Not.Empty);
    }

    [Test]
    public void AFlushOfAFailingSaveStillReturns()
    {
        File.SetAttributes(_filePath, FileAttributes.ReadOnly);
        _connectionsService.SaveConnectionsAsync();

        // Shutdown must complete whether or not the write can.
        Assert.That(_connectionsService.FlushPendingSaves(TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(WarningsAndErrors(), Is.Not.Empty);
    }

    #endregion

    #region Batching

    [Test]
    public void ABatchDrainsItsDeferredSave()
    {
        using (_connectionsService.BatchedSavingContext())
        {
            _connectionsService.SaveConnections();
            Assert.That(_savesPerformed, Is.Zero, "the save should be deferred, not performed");
        }

        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    [Test]
    public void ABatchThatDefersNothingSavesNothing()
    {
        using (_connectionsService.BatchedSavingContext())
        {
        }

        Assert.That(_savesPerformed, Is.Zero,
            "a deferred request left set would make the next batch save for no reason");
    }

    [Test]
    public void ADeferredRequestIsNotReplayedByALaterBatch()
    {
        using (_connectionsService.BatchedSavingContext())
            _connectionsService.SaveConnections();

        Assert.That(_savesPerformed, Is.EqualTo(1));

        using (_connectionsService.BatchedSavingContext())
        {
        }

        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    [Test]
    public void AnInnerBatchDoesNotEndTheOuterOne()
    {
        _connectionsService.BeginBatchingSaves();
        _connectionsService.BeginBatchingSaves();

        _connectionsService.SaveConnections();

        _connectionsService.EndBatchingSaves();
        Assert.That(_savesPerformed, Is.Zero, "the outer batch is still open");

        _connectionsService.EndBatchingSaves();
        Assert.That(_savesPerformed, Is.EqualTo(1));
    }

    #endregion
}
