using System.Linq;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Tools;

/// <summary>
/// <see cref="SecureTransfer"/> had no tests before the credential change; these cover what can
/// be asserted without a server. <c>CreateClient</c> exists as a seam for exactly that: it does
/// everything <c>Connect</c> does except the network I/O.
/// </summary>
[TestFixture]
public class SecureTransferTests
{
    private const string Host = "example-host";

    private static readonly string[] PasswordThenKeyboardInteractive = ["password", "keyboard-interactive"];
    private static readonly string[] KeyboardInteractiveOnly = ["keyboard-interactive"];

    [Test]
    public void TheSftpClientAuthenticatesAsTheCredentialsEffectiveUsername()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential(@"CORP\alice", unqualifiedUsername: "alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);

        transfer.CreateClient();

        Assert.That(transfer.SftpClt!.ConnectionInfo.Username, Is.EqualTo(@"CORP\alice"));
    }

    [Test]
    public void TheClientCarriesTheHostAndPort()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp,
            port: 2222);

        transfer.CreateClient();

        Assert.Multiple(() =>
        {
            Assert.That(transfer.SftpClt!.ConnectionInfo.Host, Is.EqualTo(Host));
            Assert.That(transfer.SftpClt.ConnectionInfo.Port, Is.EqualTo(2222));
        });
    }

    [Test]
    public void ThePasswordIsOfferedAsAnAuthenticationMethod()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);

        transfer.CreateClient();

        Assert.That(transfer.SftpClt!.ConnectionInfo.AuthenticationMethods.Select(m => m.Name),
            Is.EqualTo(PasswordThenKeyboardInteractive));
    }

    [Test]
    public void ACredentialWithNoSecretStillProducesAUsableClient()
    {
        // Previously this window could only ever offer the typed password, so an empty one meant
        // there was nothing to authenticate with. Keyboard-interactive is still offered.
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice"),
            SecureTransfer.SshTransferProtocol.Sftp);

        transfer.CreateClient();

        Assert.That(transfer.SftpClt!.ConnectionInfo.AuthenticationMethods.Select(m => m.Name),
            Is.EqualTo(KeyboardInteractiveOnly));
    }

    [Test]
    public void ScpBuildsAnScpClientAndNoSftpClient()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Scp);

        transfer.CreateClient();

        Assert.Multiple(() =>
        {
            Assert.That(transfer.ScpClt, Is.Not.Null);
            Assert.That(transfer.SftpClt, Is.Null);
        });
    }

    [Test]
    public void SftpBuildsAnSftpClientAndNoScpClient()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);

        transfer.CreateClient();

        Assert.Multiple(() =>
        {
            Assert.That(transfer.SftpClt, Is.Not.Null);
            Assert.That(transfer.ScpClt, Is.Null);
        });
    }

    [Test]
    public void SurroundingQuotesAreStrippedFromBothPaths()
    {
        using SecureTransfer transfer = new(
            Host, 22, new ResolvedSshCredential("alice"), SecureTransfer.SshTransferProtocol.Sftp,
            "\"C:\\local file.txt\"", "\"/remote/file.txt\"");

        Assert.Multiple(() =>
        {
            Assert.That(transfer.SrcFile, Is.EqualTo(@"C:\local file.txt"));
            Assert.That(transfer.DstFile, Is.EqualTo("/remote/file.txt"));
        });
    }

    [Test]
    public void UploadingBeforeConnectingDoesNotThrow()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);

        Assert.DoesNotThrowAsync(async () => await transfer.UploadAsync());
    }

    [Test]
    public void DisposingScrubsTheCredentialItWasGiven()
    {
        ResolvedSshCredential credential = new("alice", secret: "secret123");
        char[] secret = credential.SecretBuffer!;

        using (SecureTransfer transfer = Create(credential, SecureTransfer.SshTransferProtocol.Sftp))
        {
            transfer.CreateClient();
        }

        Assert.That(secret, Is.All.EqualTo('\0'));
    }

    [Test]
    public void DisposingTwiceIsHarmless()
    {
        SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);
        transfer.CreateClient();

        transfer.Dispose();

        Assert.DoesNotThrow(transfer.Dispose);
    }

    private static SecureTransfer Create(ResolvedSshCredential credential,
        SecureTransfer.SshTransferProtocol protocol,
        int port = 22) =>
        new(Host, port, credential, protocol, "local.txt", "/remote.txt");
}