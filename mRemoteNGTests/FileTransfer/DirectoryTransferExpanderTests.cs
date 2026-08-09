using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers <c>specs/recursive-directory-transfers/spec.md</c>. Every case runs against scripted
/// filesystems, so none of it needs a server.
/// </summary>
[TestFixture]
public class DirectoryTransferExpanderTests
{
    private const string DestinationRoot = "/dest";

    private static DirectoryTransferExpander Expander(
        FakeFileSystemBrowser source,
        FakeFileSystemBrowser destination,
        ITransferConflictResolver? resolver = null,
        int maximumDepth = DirectoryTransferExpander.DefaultMaximumDepth) =>
        new(source, destination,
            resolver ?? new FakeTransferConflictResolver(TransferConflictResolution.OverwriteAll),
            maximumDepth);

    private static async Task<List<TransferPlanItem>> CollectAsync(
        DirectoryTransferExpander expander,
        FileSystemEntry entry,
        string destination = DestinationRoot,
        CancellationToken cancellationToken = default)
    {
        List<TransferPlanItem> items = [];

        await foreach (TransferPlanItem item in expander.ExpandAsync(entry, destination, cancellationToken))
            items.Add(item);

        return items;
    }

    /// <summary>A destination that already holds <c>/dest</c> and nothing else.</summary>
    private static FakeFileSystemBrowser EmptyDestination() =>
        new FakeFileSystemBrowser().WithDirectory(DestinationRoot);

    // ---- expanding a tree --------------------------------------------------------

    [Test]
    public async Task EveryFileBeneathADirectoryIsQueued()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "one.txt")
            .WithFile("/src/tree", "two.txt");

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/one.txt", "/src/tree/two.txt"];
        Assert.That(items.Select(i => i.SourcePath), Is.EquivalentTo(expected));
    }

    [Test]
    public async Task DestinationPathsMirrorPositionInTheTree()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithSubdirectory("/src/tree", "deep")
            .WithFile("/src/tree/deep", "buried.txt");

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "tree"));

        Assert.That(items.Single().DestinationPath, Is.EqualTo("/dest/tree/deep/buried.txt"));
    }

    [Test]
    public async Task NestedDirectoriesAreAllExpanded()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "top.txt")
            .WithSubdirectory("/src/tree", "middle")
            .WithFile("/src/tree/middle", "middle.txt")
            .WithSubdirectory("/src/tree/middle", "bottom")
            .WithFile("/src/tree/middle/bottom", "bottom.txt");

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "tree"));

        Assert.That(items, Has.Count.EqualTo(3));
    }

    /// <summary>
    /// The bug design decision D2 exists to prevent: joining a Windows source path onto a POSIX
    /// destination produces a single remote file literally named <c>sub\file.txt</c>, which is a
    /// legal name on a unix server and so fails silently.
    /// </summary>
    [Test]
    public async Task DestinationSeparatorsComeFromTheDestination()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser('\\')
            .WithSubdirectory(@"C:\src", "tree")
            .WithSubdirectory(@"C:\src\tree", "sub")
            .WithFile(@"C:\src\tree\sub", "file.txt");

        FakeFileSystemBrowser destination = new FakeFileSystemBrowser('/').WithDirectory("/dest");

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, destination), source.Entry(@"C:\src", "tree"));

        Assert.That(items.Single().DestinationPath, Is.EqualTo("/dest/tree/sub/file.txt"));
    }

    [Test]
    public async Task ADirectlySelectedFileIsQueuedOnItsOwn()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser().WithFile("/src", "alone.txt");

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "alone.txt"));

        Assert.That(items.Single().DestinationPath, Is.EqualTo("/dest/alone.txt"));
    }

    // ---- destination directories -------------------------------------------------

    [Test]
    public async Task AnEmptySourceDirectoryIsStillCreated()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithSubdirectory("/src/tree", "empty");

        FakeFileSystemBrowser destination = EmptyDestination();

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, destination), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(destination.EnsuredDirectories, Does.Contain("/dest/tree/empty"));
        });
    }

    [Test]
    public async Task ADirectoryThatCannotBeCreatedAbandonsOnlyThatBranch()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithSubdirectory("/src/tree", "blocked")
            .WithFile("/src/tree/blocked", "unreachable.txt")
            .WithSubdirectory("/src/tree", "fine")
            .WithFile("/src/tree/fine", "reachable.txt");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.UncreatablePaths.Add("/dest/tree/blocked");

        DirectoryTransferExpander expander = Expander(source, destination);
        List<string> skipped = [];
        expander.Skipped += (_, message) => skipped.Add(message);

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/fine/reachable.txt"];
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
            Assert.That(skipped, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ADirectoryThatCannotBeListedLeavesSiblingsAlone()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithSubdirectory("/src/tree", "secret")
            .WithSubdirectory("/src/tree", "open")
            .WithFile("/src/tree/open", "readable.txt");

        source.UnreadablePaths.Add("/src/tree/secret");

        DirectoryTransferExpander expander = Expander(source, EmptyDestination());
        List<string> skipped = [];
        expander.Skipped += (_, message) => skipped.Add(message);

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/open/readable.txt"];
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
            Assert.That(skipped, Has.Count.EqualTo(1));
        });
    }

    // ---- links and depth ---------------------------------------------------------

    [Test]
    public async Task ALinkToAFileIsTransferredAsAFile()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithLink("/src/tree", "shortcut", targetIsDirectory: false);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "tree"));

        Assert.That(items.Single().SourcePath, Is.EqualTo("/src/tree/shortcut"));
    }

    [Test]
    public async Task ALinkToADirectoryIsReportedAndNotFollowed()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithLink("/src/tree", "loop", targetIsDirectory: true);

        DirectoryTransferExpander expander = Expander(source, EmptyDestination());
        List<string> skipped = [];
        expander.Skipped += (_, message) => skipped.Add(message);

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(skipped.Single(), Does.Contain("link"));
        });
    }

    /// <summary>
    /// A link pointing at its own ancestor is what makes a tree infinite. The listing reports it as
    /// an ordinary entry, so only the link rule stops this expanding until memory runs out.
    /// </summary>
    [Test]
    public async Task ALinkCycleTerminates()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "real.txt")
            .WithLink("/src/tree", "back", targetIsDirectory: true);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination()), source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/real.txt"];
        Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
    }

    [Test]
    public async Task TheDepthLimitStopsDescentAndIsReported()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "shallow.txt")
            .WithSubdirectory("/src/tree", "deeper")
            .WithFile("/src/tree/deeper", "toodeep.txt");

        DirectoryTransferExpander expander = Expander(source, EmptyDestination(), maximumDepth: 1);
        List<string> skipped = [];
        expander.Skipped += (_, message) => skipped.Add(message);

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/shallow.txt"];
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
            Assert.That(skipped.Single(), Does.Contain("not expanded in full"));
        });
    }

    // ---- cancellation ------------------------------------------------------------

    [Test]
    public void CancellingStopsTheWalk()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "one.txt");

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.That(async () => await CollectAsync(Expander(source, EmptyDestination()),
                source.Entry("/src", "tree"),
                cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // ---- collisions --------------------------------------------------------------

    [Test]
    public async Task ATransferWithNoCollisionsNeverAsks()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "new.txt");

        FakeTransferConflictResolver resolver = new(TransferConflictResolution.OverwriteAll);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, EmptyDestination(), resolver), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(resolver.TimesAsked, Is.Zero);
        });
    }

    /// <summary>
    /// A directory the expander just created cannot contain a collision, so listing it would be a
    /// round trip spent learning nothing — the saving design decision D7 is built around.
    /// </summary>
    [Test]
    public async Task ANewlyCreatedDestinationDirectoryIsNotListed()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "new.txt");

        FakeFileSystemBrowser destination = EmptyDestination();

        await CollectAsync(Expander(source, destination), source.Entry("/src", "tree"));

        Assert.That(destination.ListedPaths, Does.Not.Contain("/dest/tree"));
    }

    [Test]
    public async Task SeveralCollisionsAskExactlyOnce()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "a.txt")
            .WithFile("/src/tree", "b.txt");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "a.txt").WithFile("/dest/tree", "b.txt");

        FakeTransferConflictResolver resolver = new(TransferConflictResolution.OverwriteAll);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, destination, resolver), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(resolver.TimesAsked, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SkipAllQueuesNothingThatExists()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "existing.txt")
            .WithFile("/src/tree", "fresh.txt");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "existing.txt");

        DirectoryTransferExpander expander =
            Expander(source, destination, new FakeTransferConflictResolver(TransferConflictResolution.SkipAll));

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/fresh.txt"];
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
            Assert.That(expander.SkippedExistingCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CancellingAtThePromptStopsEverything()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "existing.txt")
            .WithFile("/src/tree", "later.txt");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "existing.txt");

        DirectoryTransferExpander expander =
            Expander(source, destination, new FakeTransferConflictResolver(TransferConflictResolution.Cancel));

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(expander.WasCancelledByUser, Is.True);
        });
    }

    [Test]
    public async Task OverwriteIfNewerQueuesOnlyTheNewerSource()
    {
        DateTime old = new(2026, 1, 1, 12, 0, 0);

        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "newer.txt", modified: old.AddMinutes(5))
            .WithFile("/src/tree", "older.txt", modified: old.AddMinutes(-5));

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "newer.txt", modified: old)
            .WithFile("/dest/tree", "older.txt", modified: old);

        DirectoryTransferExpander expander = Expander(
            source, destination, new FakeTransferConflictResolver(TransferConflictResolution.OverwriteIfNewer));

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        string[] expected = ["/src/tree/newer.txt"];
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.SourcePath), Is.EqualTo(expected));
            Assert.That(expander.SkippedExistingCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Inside the tolerance is "not newer". Without it, FAT's two-second granularity would make a
    /// file look newer than its own identical copy.
    /// </summary>
    [Test]
    public async Task ADifferenceInsideTheToleranceIsNotNewer()
    {
        DateTime moment = new(2026, 1, 1, 12, 0, 0);

        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "barely.txt",
                modified: moment + DirectoryTransferExpander.TimestampSkewTolerance);

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "barely.txt", modified: moment);

        DirectoryTransferExpander expander = Expander(
            source, destination, new FakeTransferConflictResolver(TransferConflictResolution.OverwriteIfNewer));

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        Assert.That(items, Is.Empty);
    }

    [Test]
    public async Task ADifferenceOutsideTheToleranceIsNewer()
    {
        DateTime moment = new(2026, 1, 1, 12, 0, 0);

        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "barely.txt",
                modified: moment + DirectoryTransferExpander.TimestampSkewTolerance.Add(TimeSpan.FromSeconds(1)));

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "barely.txt", modified: moment);

        DirectoryTransferExpander expander = Expander(
            source, destination, new FakeTransferConflictResolver(TransferConflictResolution.OverwriteIfNewer));

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        Assert.That(items, Has.Count.EqualTo(1));
    }

    // ---- case sensitivity --------------------------------------------------------

    [Test]
    public async Task ACaseInsensitiveDestinationTreatsDifferingCaseAsACollision()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "Readme.md");

        FakeFileSystemBrowser destination = new FakeFileSystemBrowser('/', caseSensitive: false)
            .WithDirectory("/dest");
        destination.WithFile("/dest/tree", "README.md");

        FakeTransferConflictResolver resolver = new(TransferConflictResolution.SkipAll);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, destination, resolver), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(resolver.TimesAsked, Is.EqualTo(1));
            Assert.That(items, Is.Empty);
        });
    }

    [Test]
    public async Task ACaseSensitiveDestinationTreatsDifferingCaseAsANewFile()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "Readme.md");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "README.md");

        FakeTransferConflictResolver resolver = new(TransferConflictResolution.SkipAll);

        List<TransferPlanItem> items =
            await CollectAsync(Expander(source, destination, resolver), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(resolver.TimesAsked, Is.Zero);
            Assert.That(items, Has.Count.EqualTo(1));
        });
    }

    // ---- odd collisions ----------------------------------------------------------

    [Test]
    public async Task AFileCollidingWithADirectoryIsSkippedWithoutAsking()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "clash");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithSubdirectory("/dest/tree", "clash");

        FakeTransferConflictResolver resolver = new(TransferConflictResolution.OverwriteAll);

        DirectoryTransferExpander expander = Expander(source, destination, resolver);
        List<string> skipped = [];
        expander.Skipped += (_, message) => skipped.Add(message);

        List<TransferPlanItem> items = await CollectAsync(expander, source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.Empty);
            Assert.That(resolver.TimesAsked, Is.Zero);
            Assert.That(skipped.Single(), Does.Contain("directory of that name"));
        });
    }

    /// <summary>
    /// One instance is one transfer. A fresh expander asking again is what stops an "overwrite all"
    /// from silently outliving the gesture the user gave it for.
    /// </summary>
    [Test]
    public async Task ASecondExpanderAsksAgain()
    {
        FakeFileSystemBrowser source = new FakeFileSystemBrowser()
            .WithSubdirectory("/src", "tree")
            .WithFile("/src/tree", "existing.txt");

        FakeFileSystemBrowser destination = EmptyDestination();
        destination.WithFile("/dest/tree", "existing.txt");

        FakeTransferConflictResolver first = new(TransferConflictResolution.OverwriteAll);
        FakeTransferConflictResolver second = new(TransferConflictResolution.OverwriteAll);

        await CollectAsync(Expander(source, destination, first), source.Entry("/src", "tree"));
        await CollectAsync(Expander(source, destination, second), source.Entry("/src", "tree"));

        Assert.Multiple(() =>
        {
            Assert.That(first.TimesAsked, Is.EqualTo(1));
            Assert.That(second.TimesAsked, Is.EqualTo(1));
        });
    }
}