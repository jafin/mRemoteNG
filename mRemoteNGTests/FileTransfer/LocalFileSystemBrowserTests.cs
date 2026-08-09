using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers "the local filesystem is browsable in its own pane" in
/// <c>specs/sftp-browser-panel/spec.md</c>. Uses a real temporary directory: the point is the
/// interaction with the filesystem, which a substitute would not exercise.
/// </summary>
[TestFixture]
public class LocalFileSystemBrowserTests
{
    private static readonly string[] NotesAndDocs = ["notes.txt", "docs"];

    private string _root = null!;
    private LocalFileSystemBrowser _browser = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "mrng-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _browser = new LocalFileSystemBrowser(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A test left a handle open; the temp directory is the OS's problem now.
        }
    }

    [Test]
    public async Task EnsuringANewDirectoryReportsThatItCreatedIt()
    {
        string target = Path.Combine(_root, "fresh");

        bool created = await _browser.EnsureDirectoryAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(Directory.Exists(target), Is.True);
        });
    }

    /// <summary>
    /// Called once per directory of a transferred tree, and a tree transferred twice hits every one
    /// of them a second time. Failing there would abandon the branch.
    /// </summary>
    [Test]
    public async Task EnsuringAnExistingDirectoryIsHarmlessAndSaysSo()
    {
        string target = Path.Combine(_root, "already");
        Directory.CreateDirectory(target);

        bool created = await _browser.EnsureDirectoryAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(Directory.Exists(target), Is.True);
        });
    }

    [Test]
    public async Task AnOrdinaryDirectoryIsNotReportedAsALink()
    {
        string target = Path.Combine(_root, "plain");
        Directory.CreateDirectory(target);

        var entries = await _browser.ListAsync(_root);

        Assert.That(entries.Single(e => string.Equals(e.Name, "plain", StringComparison.Ordinal)).IsSymbolicLink,
            Is.False);
    }

    [Test]
    public async Task FilesAndDirectoriesAreListed()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_root, "docs"));

        var entries = await _browser.ListAsync(_root);

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(e => e.Name), Is.EquivalentTo(NotesAndDocs));
            Assert.That(entries.Single(e => string.Equals(e.Name, "docs", StringComparison.Ordinal)).IsDirectory, Is.True);
            Assert.That(entries.Single(e => string.Equals(e.Name, "notes.txt", StringComparison.Ordinal)).IsDirectory, Is.False);
        });
    }

    [Test]
    public async Task AFileReportsItsSize()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "hello", Encoding.ASCII);

        var entries = await _browser.ListAsync(_root);

        Assert.That(entries.Single().Length, Is.EqualTo(5));
    }

    [Test]
    public async Task ADirectoryReportsNoSize()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs"));

        var entries = await _browser.ListAsync(_root);

        Assert.That(entries.Single().Length, Is.Zero);
    }

    [Test]
    public async Task AHiddenFileIsMarkedHidden()
    {
        string path = Path.Combine(_root, "secret.txt");
        File.WriteAllText(path, "x");
        File.SetAttributes(path, FileAttributes.Hidden);

        var entries = await _browser.ListAsync(_root);

        Assert.That(entries.Single().IsHidden, Is.True);
    }

    [Test]
    public void AMissingDirectoryFailsRatherThanReturningNothing()
    {
        // An empty listing would say "this directory is empty", which is a different and wrong
        // statement from "this directory could not be read".
        string missing = Path.Combine(_root, "no-such-dir");

        Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await _browser.ListAsync(missing));
    }

    [Test]
    public void PermissionsAreNotClaimedForTheLocalSide()
    {
        // Windows ACLs do not map onto the rwxr-xr-x rendering the remote pane shows.
        Assert.That(_browser.SupportsPermissions, Is.False);
    }

    [Test]
    public async Task NoPermissionStringIsProduced()
    {
        File.WriteAllText(Path.Combine(_root, "f.txt"), "x");

        var entries = await _browser.ListAsync(_root);

        Assert.That(entries.Single().Permissions, Is.Empty);
    }

    // ---- paths -------------------------------------------------------------------

    [Test]
    public void CombineUsesTheLocalSeparator()
    {
        Assert.That(_browser.Combine(@"C:\dir", "file.txt"), Is.EqualTo(@"C:\dir\file.txt"));
    }

    [Test]
    public void GetParentDropsTheLastSegment()
    {
        Assert.That(_browser.GetParentPath(@"C:\dir\sub"), Is.EqualTo(@"C:\dir"));
    }

    [Test]
    public void ATrailingSeparatorDoesNotDefeatGetParent()
    {
        Assert.That(_browser.GetParentPath(@"C:\dir\sub\"), Is.EqualTo(@"C:\dir"));
    }

    [Test]
    public void ADriveRootIsItsOwnParent()
    {
        // Otherwise repeated "up" presses walk off the top of the filesystem.
        Assert.That(_browser.GetParentPath(@"C:\"), Is.EqualTo(@"C:\"));
    }

    // ---- mutation ------------------------------------------------------------------

    [Test]
    public async Task ADirectoryCanBeCreated()
    {
        await _browser.CreateDirectoryAsync(Path.Combine(_root, "new-dir"));

        Assert.That(Directory.Exists(Path.Combine(_root, "new-dir")), Is.True);
    }

    [Test]
    public async Task AnEmptyFileCanBeCreated()
    {
        await _browser.CreateFileAsync(Path.Combine(_root, "new.txt"));

        Assert.That(File.Exists(Path.Combine(_root, "new.txt")), Is.True);
    }

    [Test]
    public void CreatingAFileThatExistsFailsRatherThanTruncating()
    {
        string path = Path.Combine(_root, "existing.txt");
        File.WriteAllText(path, "important");

        Assert.ThrowsAsync<IOException>(async () => await _browser.CreateFileAsync(path));
        Assert.That(File.ReadAllText(path), Is.EqualTo("important"));
    }

    [Test]
    public async Task AFileCanBeRenamed()
    {
        string from = Path.Combine(_root, "old.txt");
        File.WriteAllText(from, "x");

        await _browser.RenameAsync(from, Path.Combine(_root, "new.txt"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(from), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "new.txt")), Is.True);
        });
    }

    [Test]
    public async Task AFileCanBeDeleted()
    {
        string path = Path.Combine(_root, "doomed.txt");
        File.WriteAllText(path, "x");
        var entry = (await _browser.ListAsync(_root)).Single();

        await _browser.DeleteAsync(entry);

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task ANonEmptyDirectoryIsNotDeletedSilently()
    {
        // Matches the remote side: removing a tree the user has not seen is not what a delete
        // button should do.
        string dir = Path.Combine(_root, "full");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "child.txt"), "x");
        var entry = (await _browser.ListAsync(_root)).Single();

        Assert.ThrowsAsync<IOException>(async () => await _browser.DeleteAsync(entry));
        Assert.That(Directory.Exists(dir), Is.True);
    }

    // ---- transfer plumbing -------------------------------------------------------------

    [Test]
    public async Task AFileCanBeReadForTransfer()
    {
        File.WriteAllText(Path.Combine(_root, "src.txt"), "payload");

        await using Stream stream = await _browser.OpenReadAsync(Path.Combine(_root, "src.txt"));
        using StreamReader reader = new(stream);

        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("payload"));
    }

    [Test]
    public async Task AFileCanBeWrittenWithProgress()
    {
        byte[] payload = Encoding.ASCII.GetBytes(new string('x', 5000));
        using MemoryStream source = new(payload);
        long lastReported = 0;

        await _browser.WriteAsync(source, Path.Combine(_root, "dest.txt"), payload.Length,
            new Progress<long>(v => Interlocked.Exchange(ref lastReported, v)));

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(Path.Combine(_root, "dest.txt")).Length, Is.EqualTo(payload.Length));
            Assert.That(Volatile.Read(ref lastReported), Is.GreaterThan(0));
        });
    }

    [Test]
    public void NullArgumentsAreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _browser.ListAsync(null!));
            Assert.Throws<ArgumentNullException>(() => _browser.Combine(null!, "x"));
            Assert.Throws<ArgumentNullException>(() => _browser.GetParentPath(null!));
        });
    }
}