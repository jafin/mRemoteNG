using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// Rename, delete, create — and the refusals the server issues, which are as much the contract as
/// the successes.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionFileOperationTests : SftpIntegrationTestBase
{
    [Test]
    public async Task RenameMovesTheEntry()
    {
        await ExecAsync("touch", ContainerPath("before.txt"));
        SftpSession session = await ConnectedSessionAsync();

        await session.RenameAsync(RemotePath("before.txt"), RemotePath("after.txt"));

        IEnumerable<string> names = (await session.ListDirectoryAsync(RemoteDirectory)).Select(e => e.Name);

        Assert.That(names, Does.Contain("after.txt").And.No.Member("before.txt"));
    }

    [Test]
    public async Task DeleteRemovesAFileAndAnEmptyDirectory()
    {
        await ExecAsync("sh", "-c", $"cd {ContainerDirectory} && touch doomed.txt && mkdir doomed-dir");
        SftpSession session = await ConnectedSessionAsync();

        Dictionary<string, SftpEntry> byName =
            (await session.ListDirectoryAsync(RemoteDirectory)).ToDictionary(e => e.Name);

        await session.DeleteAsync(byName["doomed.txt"]);
        await session.DeleteAsync(byName["doomed-dir"]);

        Assert.That(await session.ListDirectoryAsync(RemoteDirectory), Is.Empty);
    }

    [Test]
    public async Task CreateDirectoryAndCreateFileBothShowUpInALaterListing()
    {
        SftpSession session = await ConnectedSessionAsync();

        await session.CreateDirectoryAsync(RemotePath("made-dir"));
        await session.CreateFileAsync(RemotePath("made-file.txt"));

        Dictionary<string, SftpEntry> byName =
            (await session.ListDirectoryAsync(RemoteDirectory)).ToDictionary(e => e.Name);

        Assert.Multiple(() =>
        {
            Assert.That(byName["made-dir"].IsDirectory, Is.True);
            Assert.That(byName["made-file.txt"].IsDirectory, Is.False);
            Assert.That(byName["made-file.txt"].Length, Is.Zero,
                "creating a file uploads nothing, so an empty one is the whole point");
        });
    }

    [Test]
    public async Task DeletingANonEmptyDirectoryFails()
    {
        // SftpSession.DeleteAsync carries a comment saying this is the intended outcome — the panel
        // reports the server's refusal rather than quietly recursing through a tree the user never
        // saw. That was a claim about a real server that nothing had ever checked.
        await ExecAsync("sh", "-c", $"mkdir -p {ContainerPath("full-dir")} && touch {ContainerPath("full-dir/child.txt")}");
        SftpSession session = await ConnectedSessionAsync();

        SftpEntry directory = (await session.ListDirectoryAsync(RemoteDirectory))
            .Single(e => e.Name == "full-dir");

        Assert.CatchAsync(async () => await session.DeleteAsync(directory));
    }

    [Test]
    public async Task WritingWherePermissionIsDeniedSurfacesTheRefusal()
    {
        // The chroot root is root-owned, so it is unwritable to the session without anything being
        // contrived. A session that swallowed this would report a save as successful.
        SftpSession session = await ConnectedSessionAsync();

        Assert.CatchAsync(async () => await session.CreateDirectoryAsync("/not-allowed"));
    }

    [Test]
    public async Task ExistsAnswersForAFileADirectoryAndSomethingAbsent()
    {
        await ExecAsync("sh", "-c", $"cd {ContainerDirectory} && touch there.txt && mkdir there-dir");
        SftpSession session = await ConnectedSessionAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await session.ExistsAsync(RemotePath("there.txt")), Is.True);
            Assert.That(await session.ExistsAsync(RemotePath("there-dir")), Is.True);
            Assert.That(await session.ExistsAsync(RemotePath("not-there")), Is.False);
        });
    }
}
