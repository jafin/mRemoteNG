using System;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.Connections.Multiuser;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Messages;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Connections.Multiuser;

/// <summary>
/// What the background reload does when the database's master password is no longer the one this
/// client holds.
/// </summary>
/// <remarks>
/// <para>
/// Reachable only since a refused password stopped falling back to the local copy. Before that
/// every database load failure was answered from the cache, so nothing thrown here could escape;
/// now the one failure that must not be answered that way travels straight out of a timer callback
/// nobody wrapped. What is on the other side of it is the global unhandled-exception window, over
/// whatever the user was doing, for a reload they never asked for.
/// </para>
/// <para>
/// The reload is a seam here rather than a real service: reaching this state for real needs a SQL
/// server, an upgraded database, and an administrator changing the master password underneath a
/// running client.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class RemoteConnectionsSyncronizerRefusedAuthTests
{
    private Action<bool, string> _originalReload = null!;
    private IConnectionsUpdateChecker _updateChecker = null!;
    private RemoteConnectionsSyncronizer _syncronizer = null!;
    private int _reloadsAttempted;

    [SetUp]
    public void Setup()
    {
        _originalReload = RemoteConnectionsSyncronizer.Reload;
        _reloadsAttempted = 0;
        Runtime.MessageCollector.ClearMessages();

        _updateChecker = Substitute.For<IConnectionsUpdateChecker>();
        _syncronizer = new RemoteConnectionsSyncronizer(_updateChecker);

        RemoteConnectionsSyncronizer.Reload = (_, _) =>
        {
            _reloadsAttempted++;
            throw new SqlAuthenticationRefusedException();
        };
    }

    [TearDown]
    public void Teardown()
    {
        RemoteConnectionsSyncronizer.Reload = _originalReload;
        _syncronizer.Dispose();
        Runtime.MessageCollector.ClearMessages();
    }

    [Test]
    public void ARefusedPasswordDoesNotEscapeTheReloadAsACrash()
    {
        _syncronizer.Enable();

        Assert.DoesNotThrow(() => RaiseDatabaseUpdateAvailable(),
            "an automatic reload is not a place to put an unhandled exception window");
        Assert.That(_reloadsAttempted, Is.EqualTo(1), "and it did try");
    }

    [Test]
    public void ARefusedPasswordSaysSoRatherThanFailingSilently()
    {
        _syncronizer.Enable();

        RaiseDatabaseUpdateAvailable();

        Assert.That(Runtime.MessageCollector.Messages.Any(message =>
                message.Class == MessageClass.WarningMsg &&
                message.Text.Contains("not accepted", StringComparison.OrdinalIgnoreCase)),
            "the user is told the connections on screen are the ones loaded earlier");
    }

    [Test]
    public void ARefusedPasswordStopsPollingRatherThanAskingAgainEveryInterval()
    {
        // The update is still pending — the reload is what would have cleared it — so the next tick
        // finds it again. Left polling, declining the prompt buys a modal password box every few
        // seconds instead of once. Loading connections deliberately builds a new synchronizer and
        // starts it again, which is the way back in.
        _syncronizer.Enable();
        Assert.That(_syncronizer.IsPolling, "precondition: it was polling");

        RaiseDatabaseUpdateAvailable();

        Assert.That(_syncronizer.IsPolling, Is.False);
    }

    [Test]
    public void ARefusedReloadIsNotRecordedAsASuccessfulSync()
    {
        _syncronizer.Enable();
        bool reloadAnnounced = false;
        _syncronizer.ConnectionsReloadedExternally += (_, _) => reloadAnnounced = true;

        RaiseDatabaseUpdateAvailable();

        Assert.Multiple(() =>
        {
            Assert.That(_syncronizer.LastExternalSync, Is.Null,
                "nothing was synced, so nothing may claim a sync time");
            Assert.That(reloadAnnounced, Is.False,
                "and the tree the windows are bound to did not change");
        });
    }

    private void RaiseDatabaseUpdateAvailable() =>
        _updateChecker.ConnectionsUpdateAvailable += Raise.Event<ConnectionsUpdateAvailableEventHandler>(
            _updateChecker,
            new ConnectionsUpdateAvailableEventArgs(Substitute.For<IDatabaseConnector>(), DateTime.UtcNow));
}
