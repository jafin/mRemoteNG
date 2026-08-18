using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Connection;

/// <summary>
/// That a store loaded from a local copy is never written back over the source it stands in for.
/// </summary>
/// <remarks>
/// <para>
/// When a database load failed, the application substituted a stale local copy and reported
/// "Loading from local cache in read-only mode". Nothing made it read-only — the only gate that
/// existed was the user's own SQL read-only preference — so the next save wrote the stale tree back
/// over the live database. Connections a colleague deleted returned, connections a colleague added
/// vanished, and every edit since the copy was taken was undone, at the one moment nobody is
/// watching for it.
/// </para>
/// <para>
/// Driven through the public save entry point rather than the private one under the lock, because
/// the batching and debounce paths are the dangerous ones: the save that does this is the one nobody
/// asked for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[NonParallelizable]
[TestFixture]
public class ConnectionsServiceFallbackCopyTests
{
    private ConnectionsService _connectionsService = null!;
    private string _file = "";

    [SetUp]
    public void Setup()
    {
        _connectionsService = new ConnectionsService(PuttySessionsManager.Instance)
        {
            IsConnectionsFileLoaded = true
        };

        _file = Path.Combine(Path.GetTempPath(), "mrng-fallback-" + Guid.NewGuid().ToString("N") + ".xml");
        Runtime.MessageCollector.ClearMessages();
    }

    [TearDown]
    public void Teardown()
    {
        try
        {
            if (File.Exists(_file))
                File.Delete(_file);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    [Test]
    public void ASaveOfACopyWritesNothing()
    {
        ConnectionTreeModel model = Store();
        model.IsFallbackCopy = true;

        _connectionsService.SaveConnections(model, useDatabase: false, new SaveFilter(), _file);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(_file), Is.False, "nothing reached the source");
            Assert.That(Refusals(), Is.GreaterThan(0), "and the user was told, without opening the log");
        });
    }

    [Test]
    public void ASaveOfARealLoadIsUnaffected()
    {
        // The other half. A refusal that fired on ordinary saves would be a far worse defect than
        // the one being fixed, and this is the assertion that would catch it.
        ConnectionTreeModel model = Store();

        _connectionsService.SaveConnections(model, useDatabase: false, new SaveFilter(), _file);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(_file), "an ordinary save still writes");
            Assert.That(Refusals(), Is.Zero);
        });
    }

    [Test]
    public void AForcedSaveOfACopyStillWritesNothing()
    {
        // forceSave exists to bypass the "nothing is loaded" guard. It must not bypass this one:
        // the reason for refusing is not that the application is in an odd state, it is that
        // writing would destroy other people's work.
        ConnectionTreeModel model = Store();
        model.IsFallbackCopy = true;

        _connectionsService.SaveConnections(model, useDatabase: false, new SaveFilter(), _file, forceSave: true);

        Assert.That(File.Exists(_file), Is.False);
    }

    [Test]
    public void ReloadingFromTheSourceRestoresSaving()
    {
        // The flag belongs to the model, so replacing the model is what clears it — there is no
        // setting left behind to restore, and nothing to forget to restore.
        ConnectionTreeModel fromCopy = Store();
        fromCopy.IsFallbackCopy = true;
        _connectionsService.SaveConnections(fromCopy, useDatabase: false, new SaveFilter(), _file);

        ConnectionTreeModel fromSource = Store();
        _connectionsService.SaveConnections(fromSource, useDatabase: false, new SaveFilter(), _file);

        Assert.That(File.Exists(_file));
    }

    private static int Refusals() =>
        Runtime.MessageCollector.Messages.Count(message =>
            message.Class == MessageClass.WarningMsg &&
            message.Text.Contains("local copy", StringComparison.Ordinal));

    private static ConnectionTreeModel Store()
    {
        ConnectionTreeModel model = new();
        RootNodeInfo root = new(RootNodeType.Connection);
        root.AddChild(new ConnectionInfo { Name = "webserver", Hostname = "web01" });
        model.AddRootNode(root);
        return model;
    }
}
