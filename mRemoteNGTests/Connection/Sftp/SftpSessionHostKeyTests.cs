using System.Runtime.Versioning;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.Security.Ssh;
using mRemoteNGTests.Connection.Protocol.SSH.Native;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Sftp;

/// <summary>
/// The file manager's connection is a second SSH connection to a host the terminal session may
/// already have verified, and it used to accept whatever key it was offered. These cover the
/// decision it now makes, driven through <c>TrustHostKey</c> so no server and no dialog is needed —
/// a wrong answer for a changed key is a security defect, not an inconvenience.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionHostKeyTests
{
    private const string Host = "host.invalid";
    private const string Algorithm = "ssh-ed25519";

    [Test]
    public void AnUnknownKeyIsRefusedWhenTheUserDeclines()
    {
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SftpSession session = Create(store, verifier);

        Assert.Multiple(() =>
        {
            Assert.That(session.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
            Assert.That(store.SaveCount, Is.Zero, "a refused key must not be recorded as accepted");
        });
    }

    [Test]
    public void AKeyAcceptedForASessionConnectsHereWithNoPrompt()
    {
        // The measured cost of opening the panel beside a session stays zero: the store is shared,
        // so the acceptance the session obtained is already here.
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SftpSession session = Create(store, verifier);

        Assert.Multiple(() =>
        {
            Assert.That(session.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.True);
            Assert.That(verifier.Asked, Is.Empty);
        });
    }

    [Test]
    public void AChangedKeyIsRefusedWhenTheUserDeclines()
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SftpSession session = Create(store, verifier);

        Assert.Multiple(() =>
        {
            Assert.That(session.TrustHostKey(Algorithm, HostKeyFingerprints.Second), Is.False);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Changed));
            Assert.That(store.Find(Host, 22, Algorithm), Is.EqualTo(HostKeyFingerprints.First),
                "a refused change must not overwrite what the user did accept");
        });
    }

    [Test]
    public void TheSameHostOnADifferentPortIsAskedAboutSeparately()
    {
        // The endpoint, not the hostname. A second daemon on another port is a different service,
        // and a prompt for it is correct behaviour rather than evidence the record was lost.
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SftpSession session = Create(store, verifier, port: 2222);

        Assert.Multiple(() =>
        {
            Assert.That(session.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
            Assert.That(verifier.Asked[0].Port, Is.EqualTo(2222));
        });
    }

    [Test]
    public void TheSameEndpointWithADifferentKeyAlgorithmIsAskedAboutSeparately()
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: false);

        using SftpSession session = Create(store, verifier);

        Assert.Multiple(() =>
        {
            Assert.That(session.TrustHostKey("ssh-rsa", HostKeyFingerprints.Second), Is.False);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown),
                "a key of another algorithm is another key, not the same one changed");
        });
    }

    [Test]
    public void AnAcceptedChangeReplacesTheStoredKeyAndTheOldOneIsThenTheChangedOne()
    {
        MemoryHostKeyStore store = new();
        store.Save(Host, 22, Algorithm, HostKeyFingerprints.First);
        RecordingHostKeyVerifier verifier = new(answer: true);

        using SftpSession session = Create(store, verifier);

        bool changeAccepted = session.TrustHostKey(Algorithm, HostKeyFingerprints.Second);

        // The host now presents what used to be stored. Nothing about it being the former key makes
        // it acceptable; if it were let through, an attacker who once held the old key could roll
        // the endpoint back to it silently.
        bool oldKeyOffered = session.TrustHostKey(Algorithm, HostKeyFingerprints.First);

        Assert.Multiple(() =>
        {
            Assert.That(changeAccepted, Is.True);
            Assert.That(oldKeyOffered, Is.True, "the verifier answered yes to both");
            Assert.That(verifier.Asked, Has.Count.EqualTo(2));
            Assert.That(verifier.Asked[1].Status, Is.EqualTo(HostKeyStatus.Changed),
                "the fingerprint that was replaced is a change like any other, not a silent match");
            Assert.That(verifier.Asked[1].PreviousFingerprint, Is.EqualTo(HostKeyFingerprints.Second));
        });
    }

    [Test]
    public void WithNoGateSuppliedAnUnknownKeyIsRefused()
    {
        // Nothing reaches this by accident — the file manager passes a gate — but a caller that
        // forgets must cost a refusal rather than a connection nobody verified.
        using SftpSession session = new(Host, 22, new ResolvedSshCredential("alice"));

        Assert.That(session.TrustHostKey(Algorithm, HostKeyFingerprints.First), Is.False);
    }

    private static SftpSession Create(
        MemoryHostKeyStore store, RecordingHostKeyVerifier verifier, int port = 22) =>
        new(Host, port, new ResolvedSshCredential("alice"),
            new HostKeyGate(store, verifier, new HostKeyDecisionLock()));
}
