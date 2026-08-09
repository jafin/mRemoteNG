using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer
{
    /// <summary>
    /// Covers <c>specs/recursive-directory-deletion/spec.md</c>. Ordering is the requirement that
    /// matters most here: a directory removed before its contents is a request the server rejects, so
    /// the order the planner yields in is the whole of the guarantee.
    /// </summary>
    [TestFixture]
    public class DirectoryDeletionPlannerTests
    {
        private static async Task<List<FileSystemEntry>> PlanAsync(
            DirectoryDeletionPlanner planner,
            FileSystemEntry entry,
            CancellationToken cancellationToken = default)
        {
            List<FileSystemEntry> planned = [];

            await foreach (FileSystemEntry item in planner.PlanAsync(entry, cancellationToken))
                planned.Add(item);

            return planned;
        }

        private static int IndexOf(List<FileSystemEntry> planned, string path) =>
            planned.FindIndex(e => string.Equals(e.FullPath, path, StringComparison.Ordinal));

        // ---- ordering ----------------------------------------------------------------

        [Test]
        public async Task ContentsAreDeletedBeforeTheirDirectory()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "a.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.That(IndexOf(planned, "/src/tree/a.txt"), Is.LessThan(IndexOf(planned, "/src/tree")));
        }

        [Test]
        public async Task TheSelectedDirectoryIsLast()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "a.txt")
                .WithSubdirectory("/src/tree", "sub")
                .WithFile("/src/tree/sub", "b.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.That(planned[^1].FullPath, Is.EqualTo("/src/tree"));
        }

        [Test]
        public async Task EveryDirectoryFollowsEverythingBeneathIt()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithSubdirectory("/src/tree", "sub")
                .WithFile("/src/tree/sub", "deep.txt")
                .WithSubdirectory("/src/tree/sub", "deeper")
                .WithFile("/src/tree/sub/deeper", "deepest.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.Multiple(() =>
            {
                Assert.That(IndexOf(planned, "/src/tree/sub/deeper/deepest.txt"),
                            Is.LessThan(IndexOf(planned, "/src/tree/sub/deeper")));
                Assert.That(IndexOf(planned, "/src/tree/sub/deeper"),
                            Is.LessThan(IndexOf(planned, "/src/tree/sub")));
                Assert.That(IndexOf(planned, "/src/tree/sub"),
                            Is.LessThan(IndexOf(planned, "/src/tree")));
            });
        }

        [Test]
        public async Task EveryEntryAppearsExactlyOnce()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "a.txt")
                .WithSubdirectory("/src/tree", "sub")
                .WithFile("/src/tree/sub", "b.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.That(planned.Select(e => e.FullPath).Distinct().Count(), Is.EqualTo(planned.Count));
        }

        // ---- trivial cases -----------------------------------------------------------

        [Test]
        public async Task AnEmptyDirectoryYieldsOnlyItself()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser().WithSubdirectory("/src", "empty");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "empty"));

            Assert.That(planned.Single().FullPath, Is.EqualTo("/src/empty"));
        }

        [Test]
        public async Task ASingleFileYieldsOnlyItself()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser().WithFile("/src", "alone.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "alone.txt"));

            Assert.That(planned.Single().FullPath, Is.EqualTo("/src/alone.txt"));
        }

        // ---- links -------------------------------------------------------------------

        /// <summary>
        /// Following a link when deleting destroys data outside the selected tree — the one mistake here
        /// that cannot be walked back.
        /// </summary>
        [Test]
        public async Task ALinkIsDeletedButNotFollowed()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithLink("/src/tree", "pointer", targetIsDirectory: true)
                .WithSubdirectory("/src", "elsewhere")
                .WithFile("/src/elsewhere", "precious.txt");

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.Multiple(() =>
            {
                Assert.That(planned.Select(e => e.FullPath), Does.Contain("/src/tree/pointer"));
                Assert.That(planned.Select(e => e.FullPath), Does.Not.Contain("/src/elsewhere/precious.txt"));
            });
        }

        [Test]
        public async Task ASelectedLinkYieldsOnlyTheLink()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithLink("/src", "pointer", targetIsDirectory: true);

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "pointer"));

            Assert.That(planned.Single().FullPath, Is.EqualTo("/src/pointer"));
        }

        [Test]
        public async Task ALinkToAnAncestorTerminates()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "real.txt")
                .WithLink("/src/tree", "back", targetIsDirectory: true);

            List<FileSystemEntry> planned =
                await PlanAsync(new DirectoryDeletionPlanner(browser), browser.Entry("/src", "tree"));

            Assert.That(planned, Has.Count.EqualTo(3));
        }

        // ---- failures ----------------------------------------------------------------

        /// <summary>
        /// A directory whose listing failed cannot be emptied, so queueing it would queue a failure.
        /// Its siblings are unaffected.
        /// </summary>
        [Test]
        public async Task ADirectoryThatCannotBeListedIsReportedAndNotQueued()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithSubdirectory("/src/tree", "secret")
                .WithSubdirectory("/src/tree", "open")
                .WithFile("/src/tree/open", "fine.txt");

            browser.UnreadablePaths.Add("/src/tree/secret");

            DirectoryDeletionPlanner planner = new(browser);
            List<string> skipped = [];
            planner.Skipped += (_, message) => skipped.Add(message);

            List<FileSystemEntry> planned = await PlanAsync(planner, browser.Entry("/src", "tree"));

            Assert.Multiple(() =>
            {
                Assert.That(planned.Select(e => e.FullPath), Does.Not.Contain("/src/tree/secret"));
                Assert.That(planned.Select(e => e.FullPath), Does.Contain("/src/tree/open/fine.txt"));
                Assert.That(planned.Select(e => e.FullPath), Does.Contain("/src/tree/open"));
                Assert.That(skipped, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task TheDepthLimitStopsDescentAndIsReported()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "shallow.txt")
                .WithSubdirectory("/src/tree", "deeper")
                .WithFile("/src/tree/deeper", "toodeep.txt");

            DirectoryDeletionPlanner planner = new(browser, maximumDepth: 1);
            List<string> skipped = [];
            planner.Skipped += (_, message) => skipped.Add(message);

            List<FileSystemEntry> planned = await PlanAsync(planner, browser.Entry("/src", "tree"));

            Assert.Multiple(() =>
            {
                Assert.That(planned.Select(e => e.FullPath), Does.Not.Contain("/src/tree/deeper/toodeep.txt"));
                Assert.That(skipped, Is.Not.Empty);
            });
        }

        [Test]
        public void CancellingStopsThePlan()
        {
            FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
                .WithSubdirectory("/src", "tree")
                .WithFile("/src/tree", "a.txt");

            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.That(async () => await PlanAsync(new DirectoryDeletionPlanner(browser),
                                                    browser.Entry("/src", "tree"),
                                                    cancellation.Token),
                        Throws.InstanceOf<OperationCanceledException>());
        }

        // ---- queue items -------------------------------------------------------------

        [Test]
        public void ADeletionItemCarriesItsEntryAndNoDestination()
        {
            FileSystemEntry entry = new("a.txt", "/src/a.txt", IsDirectory: false, Length: 42,
                                        LastWriteTime: new DateTime(2026, 1, 1), Permissions: string.Empty,
                                        IsHidden: false);

            TransferItem item = TransferItem.Deletion(TransferDirection.Download, entry);

            Assert.Multiple(() =>
            {
                Assert.That(item.Kind, Is.EqualTo(TransferOperationKind.Delete));
                Assert.That(item.DeleteTarget, Is.SameAs(entry));
                Assert.That(item.DestinationPath, Is.Empty);
                Assert.That(item.SourcePath, Is.EqualTo("/src/a.txt"));
            });
        }

        /// <summary>
        /// Rebuilding the entry from a path and a size would get this wrong twice: an empty file is
        /// indistinguishable from a directory by size, and a link would lose the flag that stops it
        /// being deleted as the directory it points at.
        /// </summary>
        [Test]
        public void ADeletionItemPreservesWhatTheEntryIs()
        {
            FileSystemEntry link = new("pointer", "/src/pointer", IsDirectory: true, Length: 0,
                                       LastWriteTime: new DateTime(2026, 1, 1), Permissions: string.Empty,
                                       IsHidden: false, IsSymbolicLink: true);

            TransferItem item = TransferItem.Deletion(TransferDirection.Download, link);

            Assert.Multiple(() =>
            {
                Assert.That(item.DeleteTarget!.IsSymbolicLink, Is.True);
                Assert.That(item.DeleteTarget.IsDirectory, Is.True);
            });
        }

        [Test]
        public void AnOrdinaryTransferItemIsNotADeletion()
        {
            TransferItem item = new(TransferDirection.Upload, "/a", "/b", 10);

            Assert.Multiple(() =>
            {
                Assert.That(item.Kind, Is.EqualTo(TransferOperationKind.Transfer));
                Assert.That(item.DeleteTarget, Is.Null);
            });
        }
    }
}
