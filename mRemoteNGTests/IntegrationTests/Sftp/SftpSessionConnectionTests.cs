using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// Connecting, failing to connect, and refusing to serve a session that never connected.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionConnectionTests : SftpIntegrationTestBase
{
    [Test]
    public async Task APasswordConnectsAndReportsTheHomeDirectory()
    {
        SftpSession session = await ConnectedSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(session.IsConnected, Is.True);
            Assert.That(session.HomeDirectory, Is.Not.Empty);
            Assert.That(session.HomeDirectory, Does.StartWith("/"),
                "the home directory is normalised to an absolute server path");
        });
    }

    [Test]
    public void AWrongPasswordFailsToConnect()
    {
        SftpSession session = NewSession(password: "not-the-password");

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<Renci.SshNet.Common.SshAuthenticationException>(
                async () => await session.ConnectAsync());
            Assert.That(session.IsConnected, Is.False,
                "a session that failed to authenticate must not report itself connected");
        });
    }

    [Test]
    public void OperationsOnASessionThatNeverConnectedThrow()
    {
        // Failing is deliberate: a listing served from a session that was never connected would be
        // presented as the state of the remote host when it is nothing of the kind.
        SftpSession session = NewSession();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                async () => await session.ListDirectoryAsync("/"));
            Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                async () => await session.ExistsAsync("/"));
            Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                async () => await session.CreateDirectoryAsync(RemotePath("x")));
        });
    }
}
