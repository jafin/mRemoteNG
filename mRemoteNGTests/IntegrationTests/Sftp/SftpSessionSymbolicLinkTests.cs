using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// Symbolic links, which no fake produces convincingly and every real server does.
/// </summary>
/// <remarks>
/// <c>ResolvesToDirectoryAsync</c> exists so the panel knows whether it is safe to descend, and its
/// interesting case — a link whose target is gone — is reachable only against something that can
/// actually hold a dangling link.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionSymbolicLinkTests : SftpIntegrationTestBase
{
    [Test]
    public async Task AListingReportsALinkAsALink()
    {
        await ExecAsync("sh", "-c",
            $"cd {ContainerDirectory} && mkdir target-dir && ln -s target-dir link-to-dir");

        SftpSession session = await ConnectedSessionAsync();
        SftpEntry link = (await session.ListDirectoryAsync(RemoteDirectory))
            .Single(e => e.Name == "link-to-dir");

        Assert.Multiple(() =>
        {
            Assert.That(link.IsSymbolicLink, Is.True);
            Assert.That(link.Permissions, Does.StartWith("l"),
                "the ls -l form leads with the entry kind, and a link is not a directory");
        });
    }

    [Test]
    public async Task ALinkToADirectoryResolvesToADirectoryAndALinkToAFileDoesNot()
    {
        await ExecAsync("sh", "-c",
            $"cd {ContainerDirectory} && mkdir target-dir && touch target-file "
            + "&& ln -s target-dir link-to-dir && ln -s target-file link-to-file");

        SftpSession session = await ConnectedSessionAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await session.ResolvesToDirectoryAsync(RemotePath("link-to-dir")), Is.True);
            Assert.That(await session.ResolvesToDirectoryAsync(RemotePath("link-to-file")), Is.False);
        });
    }

    [Test]
    public async Task ABrokenLinkResolvesToFalseRatherThanThrowing()
    {
        // The caller only wants to know whether it is safe to descend. A dangling link is not a
        // directory, and failing the whole listing over one would be worse than answering no.
        await ExecAsync("sh", "-c",
            $"cd {ContainerDirectory} && ln -s gone-away broken-link");

        SftpSession session = await ConnectedSessionAsync();

        Assert.That(await session.ResolvesToDirectoryAsync(RemotePath("broken-link")), Is.False);
    }
}
