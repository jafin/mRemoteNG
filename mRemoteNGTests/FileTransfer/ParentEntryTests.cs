using System.Linq;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers the parent-row requirement in <c>specs/file-manager-presentation/spec.md</c>.
/// </summary>
[TestFixture]
public class ParentEntryTests
{
    private static FilePaneController Controller(FakeFileSystemBrowser browser) => new(browser, true);

    [Test]
    public async Task ADirectoryBelowTheRootHasAParentRow()
    {
        FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
            .WithSubdirectory("/home", "user")
            .WithFile("/home/user", "notes.txt");

        using FilePaneController controller = Controller(browser);
        await controller.NavigateAsync("/home/user");

        Assert.Multiple(() =>
        {
            Assert.That(controller.ParentEntry, Is.Not.Null);
            Assert.That(controller.ParentEntry!.Name, Is.EqualTo(".."));
            Assert.That(controller.ParentEntry.FullPath, Is.EqualTo("/home"));
            Assert.That(controller.ParentEntry.IsParentNavigation, Is.True);
        });
    }

    /// <summary>
    /// Both browsers already answer "no parent" by returning the path itself, so neither POSIX's
    /// root nor a Windows drive root needs handling of its own.
    /// </summary>
    [Test]
    public async Task TheRootHasNoParentRow()
    {
        FakeFileSystemBrowser browser = new FakeFileSystemBrowser().WithFile("/", "readme.txt");

        using FilePaneController controller = Controller(browser);
        await controller.NavigateAsync("/");

        Assert.That(controller.ParentEntry, Is.Null);
    }

    [Test]
    public void BeforeAnyListingThereIsNoParentRow()
    {
        using FilePaneController controller = Controller(new FakeFileSystemBrowser());

        Assert.That(controller.ParentEntry, Is.Null);
    }

    /// <summary>
    /// Keeping it out of Entries is what makes the item count and size total right without
    /// subtracting it back out at every site that reads them.
    /// </summary>
    [Test]
    public async Task TheParentRowIsNotOneOfTheEntries()
    {
        FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
            .WithSubdirectory("/home", "user")
            .WithFile("/home/user", "a.txt")
            .WithFile("/home/user", "b.txt");

        using FilePaneController controller = Controller(browser);
        await controller.NavigateAsync("/home/user");

        Assert.Multiple(() =>
        {
            Assert.That(controller.Entries, Has.Count.EqualTo(2));
            Assert.That(controller.Entries.Any(e => e.IsParentNavigation), Is.False);
        });
    }

    [Test]
    public async Task NavigatingBackToTheRootClearsTheParentRow()
    {
        FakeFileSystemBrowser browser = new FakeFileSystemBrowser()
            .WithSubdirectory("/", "home")
            .WithFile("/home", "a.txt");

        using FilePaneController controller = Controller(browser);
        await controller.NavigateAsync("/home");
        await controller.NavigateAsync("/");

        Assert.That(controller.ParentEntry, Is.Null);
    }
}