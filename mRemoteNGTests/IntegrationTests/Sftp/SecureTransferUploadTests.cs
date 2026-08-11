using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// The SSH file transfer window's backend, against a real server.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SecureTransfer"/> was rewritten twice in one change: it stopped taking a raw
/// host/user/password triple and started taking a <see cref="ResolvedSshCredential"/>, and its SFTP
/// upload moved from the APM <c>BeginUploadFile</c> to SSH.NET's native <c>UploadFileAsync</c>.
/// <c>SecureTransferTests</c> covers what a credential turns into, because <c>CreateClient</c> stops
/// short of the network — but nothing ran the part that actually moves bytes. That gap is what these
/// tests close.
/// </para>
/// <para>
/// SFTP only. <c>ScpClient</c> needs a shell on the far end to run <c>scp</c>, and the fixture's
/// server offers the SFTP subsystem and nothing else. The SCP branch stays covered by the
/// server-free tests; adding a second container to reach it would buy little, since what differs
/// between the branches is which SSH.NET client is constructed and that is assertable without one.
/// </para>
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SecureTransferUploadTests : SftpIntegrationTestBase
{
    /// <summary>
    /// Big enough that the upload reads the source in more than one chunk, so progress has
    /// something to report on the way rather than only at the end.
    /// </summary>
    private const int PayloadLength = 512 * 1024;

    private string _source = string.Empty;

    [SetUp]
    public void WriteSourceFile()
    {
        _source = Path.Combine(Path.GetTempPath(), $"mrng-transfer-{Guid.NewGuid():n}.bin");
        byte[] payload = new byte[PayloadLength];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(_source, payload);
    }

    [TearDown]
    public void DeleteSourceFile()
    {
        if (File.Exists(_source))
            File.Delete(_source);
    }

    [Test]
    public async Task UploadPutsTheSourceFileOnTheServerIntact()
    {
        using SecureTransfer transfer = NewTransfer(RemotePath("upload.bin"));

        transfer.Connect();
        await transfer.UploadAsync();
        transfer.Disconnect();

        // Compared by digest through the container's own tooling rather than by reading the file
        // back through the code under test, which would pass just as happily if both directions
        // were wrong in the same way.
        string remote = await ExecAsync("sha256sum", ContainerPath("upload.bin"));

        Assert.That(remote.Split(' ')[0], Is.EqualTo(Sha256OfSource()));
    }

    [Test]
    public async Task ProgressIsReportedAsBytesMoveAndReachesTheTotal()
    {
        // The 50 ms poll this replaced could report anything at all on a fast transfer, including
        // nothing between zero and done. Counting from the source stream should produce several
        // reports for a payload this size and land exactly on the file's length.
        ConcurrentQueue<(long Transferred, long Total)> reports = new();

        using SecureTransfer transfer = NewTransfer(RemotePath("progress.bin"));
        transfer.UploadProgress += (_, e) => reports.Enqueue((e.Transferred, e.Total));

        transfer.Connect();
        await transfer.UploadAsync();
        transfer.Disconnect();

        Assert.Multiple(() =>
        {
            Assert.That(reports, Is.Not.Empty, "the upload reported no progress at all");
            Assert.That(reports.Max(r => r.Transferred), Is.EqualTo(PayloadLength));
            Assert.That(reports.Last().Total, Is.EqualTo(PayloadLength),
                "the total comes from the source file's length");
            Assert.That(reports, Has.Count.GreaterThan(1),
                "one report at the end is the timer behaviour this replaced, not byte counting");
        });
    }

    [Test]
    public void ConnectingWithTheWrongPasswordFails()
    {
        // The credential really is being offered rather than the server accepting anything. Without
        // this, a fixture that had somehow stopped requiring authentication would leave every other
        // test in this file passing.
        using SecureTransfer transfer = NewTransfer(RemotePath("never.bin"), "not-the-password");

        Assert.Throws<Renci.SshNet.Common.SshAuthenticationException>(transfer.Connect);
    }

    [Test]
    public async Task UploadingIntoADirectoryThatDoesNotExistFails()
    {
        using SecureTransfer transfer = NewTransfer($"{RemoteDirectory}/absent/file.bin");

        transfer.Connect();

        Assert.ThrowsAsync<Renci.SshNet.Common.SftpPathNotFoundException>(
            async () => await transfer.UploadAsync());

        transfer.Disconnect();

        // Nothing half-written was left behind under the test's own directory.
        string listing = await ExecAsync("ls", "-A", ContainerDirectory);
        Assert.That(listing.Trim(), Is.Empty);
    }

    private SecureTransfer NewTransfer(string destination, string? password = null) =>
        new(SftpServerFixture.Host,
            SftpServerFixture.Port,
            new ResolvedSshCredential(
                SftpServerFixture.Username,
                secret: password ?? SftpServerFixture.Password),
            SecureTransfer.SshTransferProtocol.Sftp,
            _source,
            destination,
            AcceptingGate());

    private string Sha256OfSource()
    {
        using FileStream stream = File.OpenRead(_source);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
