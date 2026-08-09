using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using mRemoteNG.UI.Controls.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer
{
    /// <summary>
    /// Covers the confirmation and cancellation behaviour of the pane's mutation commands.
    /// </summary>
    /// <remarks>
    /// The prompts are behind an interface precisely so this is testable: a command layer that
    /// showed its own dialogs could only be exercised by clicking, and "declining the confirmation
    /// leaves the file alone" is exactly the case that must not regress unnoticed.
    /// </remarks>
    [TestFixture]
    public class FilePaneCommandsTests
    {
        private static readonly string[] DeletedAAndB = ["/home/a", "/home/b"];
        private static readonly string[] ReportsFolder = ["/home/reports"];
        private static readonly string[] NewTextFile = ["/home/new.txt"];
        private static readonly string[] NotesRenamed = ["/home/notes.txt -> /home/renamed.txt"];

        private RecordingBrowser _browser = null!;
        private FilePaneController _controller = null!;
        private StubPrompts _prompts = null!;
        private FilePaneCommands _commands = null!;

        [SetUp]
        public async Task Setup()
        {
            _browser = new RecordingBrowser();
            _controller = new FilePaneController(_browser, caseSensitivePaths: true);
            _prompts = new StubPrompts();
            _commands = new FilePaneCommands(_controller, _prompts);

            await _controller.NavigateAsync("/home");
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        private static FileSystemEntry Entry(string name = "notes.txt") =>
            new(name, "/home/" + name, false, 10, DateTime.Now, "-rw-r--r--", false);

        // ---- delete ------------------------------------------------------------------

        [Test]
        public async Task DecliningTheConfirmationDeletesNothing()
        {
            _prompts.ConfirmDeleteResult = false;

            int deleted = await _commands.DeleteAsync([Entry()]);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.Zero);
                Assert.That(_browser.Deleted, Is.Empty);
            });
        }

        [Test]
        public async Task ConfirmingDeletesTheSelection()
        {
            _prompts.ConfirmDeleteResult = true;

            int deleted = await _commands.DeleteAsync([Entry("a"), Entry("b")]);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.EqualTo(2));
                Assert.That(_browser.Deleted, Is.EqualTo(DeletedAAndB));
            });
        }

        [Test]
        public async Task TheConfirmationIsAskedOnceForTheWholeSelection()
        {
            // Confirming twenty times is a prompt people learn to click through, which is worse
            // than not asking at all.
            _prompts.ConfirmDeleteResult = true;

            await _commands.DeleteAsync([Entry("a"), Entry("b"), Entry("c")]);

            Assert.Multiple(() =>
            {
                Assert.That(_prompts.ConfirmDeleteCalls, Is.EqualTo(1));
                Assert.That(_prompts.LastConfirmDeleteCount, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task AnEmptySelectionDoesNotEvenAsk()
        {
            await _commands.DeleteAsync([]);

            Assert.That(_prompts.ConfirmDeleteCalls, Is.Zero);
        }

        [Test]
        public async Task OneRefusedDeleteDoesNotAbandonTheRest()
        {
            _prompts.ConfirmDeleteResult = true;
            _browser.RefuseDeleteOf = "/home/b";

            int deleted = await _commands.DeleteAsync([Entry("a"), Entry("b"), Entry("c")]);

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.EqualTo(2));
                Assert.That(_browser.Deleted, Does.Contain("/home/c"));
            });
        }

        // ---- create ---------------------------------------------------------------------

        [Test]
        public async Task CancellingTheNameCreatesNothing()
        {
            _prompts.NameResult = null;

            bool created = await _commands.NewFolderAsync();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(_browser.CreatedDirectories, Is.Empty);
            });
        }

        [Test]
        public async Task AnEmptyNameCreatesNothing()
        {
            _prompts.NameResult = "   ";

            bool created = await _commands.NewFolderAsync();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(_browser.CreatedDirectories, Is.Empty);
            });
        }

        [Test]
        public async Task ANameCreatesTheFolderInTheCurrentDirectory()
        {
            _prompts.NameResult = "reports";

            bool created = await _commands.NewFolderAsync();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(_browser.CreatedDirectories, Is.EqualTo(ReportsFolder));
            });
        }

        [Test]
        public async Task SurroundingWhitespaceIsTrimmedFromTheName()
        {
            _prompts.NameResult = "  reports  ";

            await _commands.NewFolderAsync();

            Assert.That(_browser.CreatedDirectories, Is.EqualTo(ReportsFolder));
        }

        [Test]
        public async Task ANameCreatesTheFile()
        {
            _prompts.NameResult = "new.txt";

            bool created = await _commands.NewFileAsync();

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(_browser.CreatedFiles, Is.EqualTo(NewTextFile));
            });
        }

        // ---- rename ------------------------------------------------------------------------

        [Test]
        public async Task CancellingARenameChangesNothing()
        {
            _prompts.NameResult = null;

            bool renamed = await _commands.RenameAsync(Entry());

            Assert.Multiple(() =>
            {
                Assert.That(renamed, Is.False);
                Assert.That(_browser.Renames, Is.Empty);
            });
        }

        [Test]
        public async Task RenamingToTheSameNameChangesNothing()
        {
            // The prompt is pre-filled with the current name, so pressing OK without editing is the
            // most likely way to reach this.
            _prompts.NameResult = "notes.txt";

            bool renamed = await _commands.RenameAsync(Entry("notes.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(renamed, Is.False);
                Assert.That(_browser.Renames, Is.Empty);
            });
        }

        [Test]
        public async Task ANewNameRenamesWithinTheCurrentDirectory()
        {
            _prompts.NameResult = "renamed.txt";

            bool renamed = await _commands.RenameAsync(Entry("notes.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(renamed, Is.True);
                Assert.That(_browser.Renames, Is.EqualTo(NotesRenamed));
            });
        }

        [Test]
        public void TheRenamePromptStartsFromTheCurrentName()
        {
            _prompts.NameResult = null;

            _ = _commands.RenameAsync(Entry("notes.txt"));

            Assert.That(_prompts.LastInitialValue, Is.EqualTo("notes.txt"));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => new FilePaneCommands(null!, _prompts));
                Assert.Throws<ArgumentNullException>(() => new FilePaneCommands(_controller, null!));
                Assert.ThrowsAsync<ArgumentNullException>(async () => await _commands.RenameAsync(null!));
                Assert.ThrowsAsync<ArgumentNullException>(async () => await _commands.DeleteAsync(null!));
            });
        }

        // ---- stubs -----------------------------------------------------------------------------

        private sealed class StubPrompts : IFilePanePrompts
        {
            public string? NameResult { get; set; }

            public bool ConfirmDeleteResult { get; set; }

            public int ConfirmDeleteCalls { get; private set; }

            public int LastConfirmDeleteCount { get; private set; }

            public string? LastInitialValue { get; private set; }

            public string? AskForName(string title, string prompt, string initialValue)
            {
                LastInitialValue = initialValue;
                return NameResult;
            }

            public bool ConfirmDelete(int count)
            {
                ConfirmDeleteCalls++;
                LastConfirmDeleteCount = count;
                return ConfirmDeleteResult;
            }
        }

        private sealed class RecordingBrowser : IFileSystemBrowser
        {
            public List<string> Deleted { get; } = [];

            public List<string> CreatedDirectories { get; } = [];

            public List<string> CreatedFiles { get; } = [];

            public List<string> Renames { get; } = [];

            public string? RefuseDeleteOf { get; set; }

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

            public string GetParentPath(string path)
            {
                int slash = path.LastIndexOf('/');
                return slash <= 0 ? "/" : path[..slash];
            }

            public string Combine(string directory, string name) =>
                directory.EndsWith('/') ? directory + name : directory + "/" + name;

            public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default)
            {
                Renames.Add($"{fromPath} -> {toPath}");
                return Task.CompletedTask;
            }

            public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
            {
                if (string.Equals(entry.FullPath, RefuseDeleteOf, StringComparison.Ordinal))
                    return Task.FromException(new IOException("refused"));

                Deleted.Add(entry.FullPath);
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
            {
                CreatedDirectories.Add(path);
                return Task.CompletedTask;
            }

            public Task CreateFileAsync(string path, CancellationToken cancellationToken = default)
            {
                CreatedFiles.Add(path);
                return Task.CompletedTask;
            }

            public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult<Stream>(new MemoryStream());

            public Task WriteAsync(Stream source, string path, long? totalBytes = null,
                                   IProgress<long>? bytesTransferred = null,
                                   CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
