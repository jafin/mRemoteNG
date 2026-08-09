using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers the listing and navigation scenarios in <c>specs/sftp-browser-panel/spec.md</c>
/// against a fake filesystem, so none of it needs a server or a message pump.
/// </summary>
[TestFixture]
public class FilePaneControllerTests
{
    private static readonly string[] DocsAndNotes = ["docs", "notes.txt"];
    private static readonly string[] DirectoriesInOrder = ["docs", "zzz-dir"];

    private FakeBrowser _browser = null!;
    private FilePaneController _pane = null!;

    [SetUp]
    public void Setup()
    {
        _browser = new FakeBrowser();
        _browser.Add("/home", "docs", isDirectory: true);
        _browser.Add("/home", "notes.txt", isDirectory: false);
        _browser.Add("/home", ".bashrc", isDirectory: false, isHidden: true);
        _browser.Add("/home/docs", "report.pdf", isDirectory: false);

        _pane = new FilePaneController(_browser, caseSensitivePaths: true);
    }

    [TearDown]
    public void TearDown() => _pane.Dispose();

    // ---- listing --------------------------------------------------------------

    [Test]
    public async Task NavigatingListsTheDirectory()
    {
        await _pane.NavigateAsync("/home");

        Assert.Multiple(() =>
        {
            Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
            Assert.That(_pane.Entries.Select(e => e.Name), Is.EqualTo(DocsAndNotes));
        });
    }

    [Test]
    public async Task DirectoriesSortBeforeFiles()
    {
        _browser.Add("/home", "aaa.txt", isDirectory: false);
        _browser.Add("/home", "zzz-dir", isDirectory: true);

        await _pane.NavigateAsync("/home");

        Assert.That(_pane.Entries.TakeWhile(e => e.IsDirectory).Select(e => e.Name),
            Is.EqualTo(DirectoriesInOrder));
    }

    [Test]
    public async Task HiddenEntriesAreExcludedUntilAskedFor()
    {
        await _pane.NavigateAsync("/home");
        Assert.That(_pane.Entries.Select(e => e.Name), Does.Not.Contain(".bashrc"));

        _pane.ShowHidden = true;

        Assert.That(_pane.Entries.Select(e => e.Name), Does.Contain(".bashrc"));
    }

    [Test]
    public async Task TogglingHiddenDoesNotRelist()
    {
        // The server was already asked for everything; a round trip to change a filter would be
        // a visible pause for no reason.
        await _pane.NavigateAsync("/home");
        int listingsBefore = _browser.ListCallCount;

        _pane.ShowHidden = true;

        Assert.That(_browser.ListCallCount, Is.EqualTo(listingsBefore));
    }

    [Test]
    public async Task TogglingHiddenAnnouncesTheChange()
    {
        await _pane.NavigateAsync("/home");
        int raised = 0;
        _pane.EntriesChanged += (_, _) => raised++;

        _pane.ShowHidden = true;

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public async Task SettingHiddenToItsCurrentValueChangesNothing()
    {
        await _pane.NavigateAsync("/home");
        int raised = 0;
        _pane.EntriesChanged += (_, _) => raised++;

        _pane.ShowHidden = false;

        Assert.That(raised, Is.Zero);
    }

    // ---- failure ---------------------------------------------------------------

    [Test]
    public async Task AFailedListingKeepsTheCurrentOne()
    {
        // "Empty" and "unreadable" are different statements, and only one of them is true.
        await _pane.NavigateAsync("/home");

        await _pane.NavigateAsync("/does-not-exist");

        Assert.Multiple(() =>
        {
            Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
            Assert.That(_pane.Entries, Is.Not.Empty);
        });
    }

    [Test]
    public async Task AFailedListingIsReported()
    {
        string? reported = null;
        _pane.OperationFailed += (_, message) => reported = message;

        await _pane.NavigateAsync("/does-not-exist");

        Assert.That(reported, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task AFailedNavigationIsNotRecordedInHistory()
    {
        await _pane.NavigateAsync("/home");

        await _pane.NavigateAsync("/does-not-exist");

        Assert.That(_pane.CanGoBack, Is.False);
    }

    // ---- navigation ------------------------------------------------------------

    [Test]
    public async Task OpeningADirectoryNavigatesIntoIt()
    {
        await _pane.NavigateAsync("/home");
        FileSystemEntry docs = _pane.Entries.Single(e => e.IsDirectory);

        await _pane.OpenAsync(docs);

        Assert.That(_pane.CurrentPath, Is.EqualTo("/home/docs"));
    }

    [Test]
    public async Task OpeningAFileDoesNotNavigate()
    {
        await _pane.NavigateAsync("/home");
        FileSystemEntry file = _pane.Entries.Single(e => !e.IsDirectory);

        await _pane.OpenAsync(file);

        Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
    }

    [Test]
    public async Task UpGoesToTheParent()
    {
        await _pane.NavigateAsync("/home/docs");

        await _pane.NavigateUpAsync();

        Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
    }

    [Test]
    public async Task UpFromTheRootDoesNothing()
    {
        _browser.Add("/", "home", isDirectory: true);
        await _pane.NavigateAsync("/");

        await _pane.NavigateUpAsync();

        Assert.That(_pane.CurrentPath, Is.EqualTo("/"));
    }

    [Test]
    public async Task BackAndForwardWalkTheHistory()
    {
        await _pane.NavigateAsync("/home");
        await _pane.NavigateAsync("/home/docs");

        await _pane.GoBackAsync();
        Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));

        await _pane.GoForwardAsync();
        Assert.That(_pane.CurrentPath, Is.EqualTo("/home/docs"));
    }

    [Test]
    public async Task AFailedBackDoesNotConsumeAHistoryStep()
    {
        // The directory may have been removed since it was visited. Losing the step as well as
        // the navigation would leave Back doing nothing on the next press too.
        await _pane.NavigateAsync("/home");
        await _pane.NavigateAsync("/home/docs");
        _browser.Remove("/home");

        await _pane.GoBackAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_pane.CurrentPath, Is.EqualTo("/home/docs"));
            Assert.That(_pane.CanGoBack, Is.True);
        });
    }

    [Test]
    public async Task RefreshRelistsWithoutTouchingHistory()
    {
        await _pane.NavigateAsync("/home");
        int before = _browser.ListCallCount;

        await _pane.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_browser.ListCallCount, Is.EqualTo(before + 1));
            Assert.That(_pane.CanGoBack, Is.False);
        });
    }

    [Test]
    public async Task HomeGoesToTheBrowsersHome()
    {
        await _pane.NavigateAsync("/home/docs");

        await _pane.NavigateHomeAsync();

        Assert.That(_pane.CurrentPath, Is.EqualTo(_browser.HomePath));
    }

    [Test]
    public async Task TwoPanesNavigateIndependently()
    {
        using FilePaneController other = new(_browser, caseSensitivePaths: true);

        await _pane.NavigateAsync("/home");
        await other.NavigateAsync("/home/docs");

        Assert.Multiple(() =>
        {
            Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
            Assert.That(other.CurrentPath, Is.EqualTo("/home/docs"));
        });
    }

    // ---- overtaking ---------------------------------------------------------------

    [Test]
    public async Task AnOvertakenListingDoesNotRepaintThePane()
    {
        // A slow listing that lands after the user has moved on would look like the pane
        // jumping back to a directory they already left.
        using ManualResetEventSlim slowStarted = new();
        using ManualResetEventSlim releaseSlow = new();
        _browser.Gate("/slow", slowStarted, releaseSlow);
        _browser.Add("/slow", "stale.txt", isDirectory: false);

        Task slow = _pane.NavigateAsync("/slow");
        slowStarted.Wait(TimeSpan.FromSeconds(5));

        await _pane.NavigateAsync("/home");
        releaseSlow.Set();
        await slow;

        Assert.Multiple(() =>
        {
            Assert.That(_pane.CurrentPath, Is.EqualTo("/home"));
            Assert.That(_pane.Entries.Select(e => e.Name), Does.Not.Contain("stale.txt"));
        });
    }

    // ---- mutation ---------------------------------------------------------------------

    [Test]
    public async Task CreatingADirectoryRefreshesTheListing()
    {
        await _pane.NavigateAsync("/home");

        bool created = await _pane.CreateDirectoryAsync("new-dir");

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(_pane.Entries.Select(e => e.Name), Does.Contain("new-dir"));
        });
    }

    [Test]
    public async Task AFailedMutationIsReportedAndTheListingSurvives()
    {
        await _pane.NavigateAsync("/home");
        _browser.FailMutations = true;
        string? reported = null;
        _pane.OperationFailed += (_, m) => reported = m;

        bool created = await _pane.CreateDirectoryAsync("nope");

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(reported, Is.Not.Null);
            Assert.That(_pane.Entries, Is.Not.Empty);
        });
    }

    [Test]
    public async Task DeletingRemovesTheEntry()
    {
        await _pane.NavigateAsync("/home");
        FileSystemEntry file = _pane.Entries.Single(e => string.Equals(e.Name, "notes.txt", StringComparison.Ordinal));

        await _pane.DeleteAsync(file);

        Assert.That(_pane.Entries.Select(e => e.Name), Does.Not.Contain("notes.txt"));
    }

    [Test]
    public void ANullBrowserIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new FilePaneController(null!, caseSensitivePaths: true));
    }

    // ---- fake ----------------------------------------------------------------------------

    private sealed class FakeBrowser : IFileSystemBrowser
    {
        private readonly Dictionary<string, List<FileSystemEntry>> _tree = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ManualResetEventSlim Started, ManualResetEventSlim Release)> _gates =
            new(StringComparer.Ordinal);

        public int ListCallCount { get; private set; }

        public bool FailMutations { get; set; }

        public string HomePath => "/home";

        public bool SupportsPermissions => true;

        public char DirectorySeparator => '/';

        public bool PathsAreCaseSensitive => true;

        public Task<bool> EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void Add(string directory, string name, bool isDirectory, bool isHidden = false)
        {
            if (!_tree.TryGetValue(directory, out List<FileSystemEntry>? entries))
                _tree[directory] = entries = [];

            entries.Add(new FileSystemEntry(name, Combine(directory, name), isDirectory, 0,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local),
                isDirectory ? "drwxr-xr-x" : "-rw-r--r--", isHidden));
        }

        public void Remove(string directory) => _tree.Remove(directory);

        public void Gate(string directory, ManualResetEventSlim started, ManualResetEventSlim release)
        {
            _gates[directory] = (started, release);
            if (!_tree.ContainsKey(directory))
                _tree[directory] = [];
        }

        public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            ListCallCount++;

            if (_gates.TryGetValue(path, out var gate))
            {
                return Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
                {
                    gate.Started.Set();
                    gate.Release.Wait(cancellationToken);
                    return _tree[path].ToArray();
                }, cancellationToken);
            }

            return _tree.TryGetValue(path, out List<FileSystemEntry>? entries)
                ? Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries.ToArray())
                : Task.FromException<IReadOnlyList<FileSystemEntry>>(
                    new DirectoryNotFoundException($"No such directory: {path}"));
        }

        public string GetParentPath(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path[..slash];
        }

        public string Combine(string directory, string name) =>
            directory.EndsWith('/') ? directory + name : directory + "/" + name;

        public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
            FailMutations ? Task.FromException(new IOException("refused")) : Task.CompletedTask;

        public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
        {
            if (FailMutations)
                return Task.FromException(new IOException("refused"));

            string parent = GetParentPath(entry.FullPath);
            _tree[parent].RemoveAll(e => string.Equals(e.FullPath, entry.FullPath, StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailMutations)
                return Task.FromException(new IOException("refused"));

            Add(GetParentPath(path), path[(path.LastIndexOf('/') + 1)..], isDirectory: true);
            return Task.CompletedTask;
        }

        public Task CreateFileAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailMutations)
                return Task.FromException(new IOException("refused"));

            Add(GetParentPath(path), path[(path.LastIndexOf('/') + 1)..], isDirectory: false);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task WriteAsync(Stream source, string path, long? totalBytes = null,
            IProgress<long>? bytesTransferred = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}