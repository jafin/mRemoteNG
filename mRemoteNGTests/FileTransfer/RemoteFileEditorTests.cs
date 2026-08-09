using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers "a remote file can be edited locally and written back" in
/// <c>specs/sftp-browser-panel/spec.md</c>.
/// </summary>
/// <remarks>
/// Change detection compares a snapshot rather than watching the file, so these tests simply
/// write to the local copy — no watcher, no debounce, no timing.
/// </remarks>
[TestFixture]
public class RemoteFileEditorTests
{
    private string _root = null!;
    private FakeRemote _remote = null!;
    private StubEditor _editor = null!;
    private StubEditPrompts _prompts = null!;
    private RemoteFileEditor _subject = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "mrng-edit-" + Guid.NewGuid().ToString("N"));
        _remote = new FakeRemote();
        _remote.Files["/home/notes.txt"] = "original";
        _editor = new StubEditor();
        _prompts = new StubEditPrompts();
        _subject = new RemoteFileEditor(_remote, _editor, _prompts, _root);
    }

    [TearDown]
    public void TearDown()
    {
        _subject.Dispose();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static FileSystemEntry Notes() =>
        new("notes.txt", "/home/notes.txt", false, 8, DateTime.Now, "-rw-r--r--", false);

    // ---- opening ----------------------------------------------------------------

    [Test]
    public async Task OpeningDownloadsTheFileAndLaunchesTheEditor()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(session.LocalPath), Is.True);
            Assert.That(File.ReadAllText(session.LocalPath), Is.EqualTo("original"));
            Assert.That(_editor.Opened, Is.EqualTo(new[] { session.LocalPath }));
        });
    }

    [Test]
    public async Task TheLocalCopyKeepsTheRemoteFilename()
    {
        // An editor's syntax highlighting and an interpreter's shebang both key off the
        // extension; a mangled temp name breaks both.
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());

        Assert.That(Path.GetFileName(session.LocalPath), Is.EqualTo("notes.txt"));
    }

    [Test]
    public async Task TwoEditsOfTheSameNameDoNotCollide()
    {
        RemoteFileEditSession first = await _subject.OpenAsync(Notes());
        _remote.Files["/other/notes.txt"] = "different";
        RemoteFileEditSession second = await _subject.OpenAsync(
            new FileSystemEntry("notes.txt", "/other/notes.txt", false, 9, DateTime.Now, "-rw-r--r--", false));

        Assert.Multiple(() =>
        {
            Assert.That(second.LocalPath, Is.Not.EqualTo(first.LocalPath));
            Assert.That(File.ReadAllText(first.LocalPath), Is.EqualTo("original"));
            Assert.That(File.ReadAllText(second.LocalPath), Is.EqualTo("different"));
        });
    }

    [Test]
    public void AFailedDownloadLeavesNoSession()
    {
        Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _subject.OpenAsync(
                new FileSystemEntry("missing.txt", "/home/missing.txt", false, 0, DateTime.Now, "", false)));

        Assert.That(_subject.Sessions, Is.Empty);
    }

    // ---- change detection ---------------------------------------------------------

    [Test]
    public async Task AnUntouchedFileIsNotConsideredChanged()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());

        Assert.That(session.HasLocalChanges, Is.False);
    }

    [Test]
    public async Task EditingTheLocalCopyIsNoticed()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());

        File.WriteAllText(session.LocalPath, "edited content");

        Assert.That(session.HasLocalChanges, Is.True);
    }

    // ---- writing back ----------------------------------------------------------------

    [Test]
    public async Task AnUnchangedFileIsNotOfferedForUpload()
    {
        await _subject.OpenAsync(Notes());

        int uploaded = await _subject.WriteBackChangedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(uploaded, Is.Zero);
            Assert.That(_prompts.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task DecliningLeavesTheRemoteFileAlone()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());
        File.WriteAllText(session.LocalPath, "edited content");
        _prompts.Result = false;

        int uploaded = await _subject.WriteBackChangedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(uploaded, Is.Zero);
            Assert.That(_prompts.Calls, Is.EqualTo(1));
            Assert.That(_remote.Files["/home/notes.txt"], Is.EqualTo("original"));
        });
    }

    [Test]
    public async Task AcceptingWritesTheEditBack()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());
        File.WriteAllText(session.LocalPath, "edited content");
        _prompts.Result = true;

        int uploaded = await _subject.WriteBackChangedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(uploaded, Is.EqualTo(1));
            Assert.That(_remote.Files["/home/notes.txt"], Is.EqualTo("edited content"));
        });
    }

    [Test]
    public async Task TheSameEditIsNotOfferedTwice()
    {
        // Re-snapshotting after the upload is what stops every subsequent activation of the tab
        // asking about a file that has already been written back.
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());
        File.WriteAllText(session.LocalPath, "edited content");
        _prompts.Result = true;
        await _subject.WriteBackChangedAsync();

        int second = await _subject.WriteBackChangedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Zero);
            Assert.That(_prompts.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheUserIsAskedAboutTheFileByName()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());
        File.WriteAllText(session.LocalPath, "edited content");

        await _subject.WriteBackChangedAsync();

        Assert.That(_prompts.LastFileName, Is.EqualTo("notes.txt"));
    }

    // ---- cleanup -------------------------------------------------------------------------

    [Test]
    public async Task ClosingASessionRemovesTheLocalCopy()
    {
        RemoteFileEditSession session = await _subject.OpenAsync(Notes());
        string path = session.LocalPath;

        _subject.Close(session);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_subject.Sessions, Is.Empty);
        });
    }

    [Test]
    public async Task DisposingRemovesEveryLocalCopy()
    {
        RemoteFileEditSession first = await _subject.OpenAsync(Notes());
        _remote.Files["/home/other.txt"] = "x";
        RemoteFileEditSession second = await _subject.OpenAsync(
            new FileSystemEntry("other.txt", "/home/other.txt", false, 1, DateTime.Now, "", false));

        _subject.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(first.LocalPath), Is.False);
            Assert.That(File.Exists(second.LocalPath), Is.False);
        });
    }

    [Test]
    public async Task DisposingTwiceIsHarmless()
    {
        await _subject.OpenAsync(Notes());
        _subject.Dispose();

        Assert.DoesNotThrow(_subject.Dispose);
    }

    [Test]
    public void UploadingBeforeDownloadingIsRejected()
    {
        using RemoteFileEditSession session = new(Notes(), _remote, Path.Combine(_root, "solo"));

        Assert.ThrowsAsync<InvalidOperationException>(async () => await session.UploadAsync());
    }

    [Test]
    public void NullArgumentsAreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new RemoteFileEditor(null!, _editor, _prompts, _root));
            Assert.Throws<ArgumentNullException>(() => new RemoteFileEditor(_remote, null!, _prompts, _root));
            Assert.Throws<ArgumentNullException>(() => new RemoteFileEditor(_remote, _editor, null!, _root));
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _subject.OpenAsync(null!));
        });
    }

    // ---- stubs -----------------------------------------------------------------------------

    private sealed class StubEditor : IExternalEditor
    {
        public List<string> Opened { get; } = [];

        public void Open(string localPath) => Opened.Add(localPath);
    }

    private sealed class StubEditPrompts : IEditPrompts
    {
        public bool Result { get; set; }

        public int Calls { get; private set; }

        public string? LastFileName { get; private set; }

        public bool ConfirmUpload(string fileName)
        {
            Calls++;
            LastFileName = fileName;
            return Result;
        }
    }

    private sealed class FakeRemote : IFileSystemBrowser
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public string HomePath => "/home";

        public bool SupportsPermissions => true;

        public char DirectorySeparator => '/';

        public bool PathsAreCaseSensitive => true;

        public Task<bool> EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);

        public string GetParentPath(string path) => "/";

        public string Combine(string directory, string name) => directory + "/" + name;

        public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CreateFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            Files.TryGetValue(path, out string? content)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)))
                : Task.FromException<Stream>(new FileNotFoundException("No such file", path));

        public async Task WriteAsync(Stream source, string path, long? totalBytes = null,
            IProgress<long>? bytesTransferred = null,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(source, Encoding.UTF8, leaveOpen: true);
            Files[path] = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}