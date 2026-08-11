using System;
using System.Linq;
using System.Reflection;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Tools;
using mRemoteNGTests.Connection.Protocol.SSH.Native;
using NUnit.Framework;
using Renci.SshNet;

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
    private const string Algorithm = "ssh-ed25519";

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

    // ---- host keys -------------------------------------------------------------

    /// <summary>
    /// Both protocols, every time. <c>CreateClient</c> branches on <see
    /// cref="SecureTransfer.SshTransferProtocol"/>, and a client built down an untested branch would
    /// connect with nothing verifying the host key at all — which is exactly how this window came to
    /// have no verification for either.
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void AnUnknownKeyIsRefusedWhenTheUserDeclines(bool scp)
    {
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp));

        Assert.Multiple(() =>
        {
            Assert.That(transfer.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
            Assert.That(store.SaveCount, Is.Zero);
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void AKeyAlreadyAcceptedTransfersWithNoPrompt(bool scp)
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp));

        Assert.Multiple(() =>
        {
            Assert.That(transfer.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.True);
            Assert.That(verifier.Asked, Is.Empty);
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void AChangedKeyIsRefusedWhenTheUserDeclines(bool scp)
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp));

        Assert.Multiple(() =>
        {
            Assert.That(transfer.TrustHostKey(Algorithm, HostKeyFingerprints.Second), Is.False);
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Changed));
            Assert.That(store.Find(Host, 22, Algorithm), Is.EqualTo(HostKeyFingerprints.First));
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void TheSameHostOnADifferentPortIsAskedAboutSeparately(bool scp)
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp), port: 2222);

        Assert.Multiple(() =>
        {
            Assert.That(transfer.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
            Assert.That(verifier.Asked[0].Port, Is.EqualTo(2222));
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void ADifferentKeyAlgorithmIsAskedAboutSeparately(bool scp)
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp));

        Assert.Multiple(() =>
        {
            Assert.That(transfer.TrustHostKey("ssh-rsa", HostKeyFingerprints.Second), Is.False);
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void AnAcceptedChangeReplacesTheStoredKeyAndTheOldOneIsThenTheChangedOne(bool scp)
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: true);

        using SecureTransfer transfer = CreateWithGate(store, verifier, Protocol(scp));

        transfer.TrustHostKey(Algorithm, HostKeyFingerprints.Second);
        transfer.TrustHostKey(Algorithm, HostKeyFingerprints.First);

        Assert.Multiple(() =>
        {
            Assert.That(verifier.Asked, Has.Count.EqualTo(2));
            Assert.That(verifier.Asked[1].Status, Is.EqualTo(HostKeyStatus.Changed),
                "the fingerprint that was replaced is not privileged by having once been accepted");
            Assert.That(verifier.Asked[1].PreviousFingerprint, Is.EqualTo(HostKeyFingerprints.Second));
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void BothProtocolsProduceOneClientForTheHostKeyCallbackToBeWiredTo(bool scp)
    {
        // The subscription is made once, on this property, outside the protocol branch. That is
        // what keeps the two paths from diverging again; this asserts the property answers for
        // both, so there is no branch where the subscription silently applies to nothing.
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"), Protocol(scp));

        transfer.CreateClient();

        Assert.That(transfer.ProtocolClient, Is.Not.Null);
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void CreateClientWiresTheHostKeyCallbackForBothProtocols(bool scp)
    {
        // The gap every other test leaves open. TrustHostKey covers the decision, but deleting the
        // one line that subscribes it would leave the whole fixture green and every transfer
        // unverified — which is exactly the state this window was in before. SSH.NET raises
        // HostKeyReceived only during a real handshake, so the subscription is as far as a test
        // without a server can reach; the callback firing belongs to the integration suite.
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"), Protocol(scp));

        transfer.CreateClient();

        Assert.That(HostKeySubscriberCount(transfer.ProtocolClient!), Is.EqualTo(1));
    }

    private static int HostKeySubscriberCount(BaseClient client)
    {
        FieldInfo? backing = typeof(BaseClient).GetField("HostKeyReceived",
            BindingFlags.Instance | BindingFlags.NonPublic);

        // Loud rather than silent if SSH.NET changes shape. A guard that quietly stops guarding is
        // worse than one that never existed, so this fails and asks to be rewritten.
        Assert.That(backing, Is.Not.Null,
            "SSH.NET no longer backs HostKeyReceived with a delegate field — rewrite this check "
            + "rather than deleting it; it is the only assertion that the callback is wired at all.");

        return ((Delegate?)backing!.GetValue(client))?.GetInvocationList().Length ?? 0;
    }

    [Test]
    public void WithNoGateSuppliedAnUnknownKeyIsRefused()
    {
        using SecureTransfer transfer = Create(
            new ResolvedSshCredential("alice", secret: "secret123"),
            SecureTransfer.SshTransferProtocol.Sftp);

        Assert.That(transfer.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
    }

    private static SecureTransfer Create(ResolvedSshCredential credential,
        SecureTransfer.SshTransferProtocol protocol,
        int port = 22) =>
        new(Host, port, credential, protocol, "local.txt", "/remote.txt");

    private static SecureTransfer CreateWithGate(MemoryHostKeyStore store,
        RecordingHostKeyVerifier verifier,
        SecureTransfer.SshTransferProtocol protocol,
        int port = 22) =>
        new(Host, port, new ResolvedSshCredential("alice"), protocol, "local.txt", "/remote.txt",
            new HostKeyGate(store, verifier, new HostKeyDecisionLock()));

    /// <summary>
    /// The protocol under test, taken as a bool because <see cref="SecureTransfer"/> is internal and
    /// a public test method cannot declare a parameter of its nested enum.
    /// </summary>
    private static SecureTransfer.SshTransferProtocol Protocol(bool scp) =>
        scp ? SecureTransfer.SshTransferProtocol.Scp : SecureTransfer.SshTransferProtocol.Sftp;
}