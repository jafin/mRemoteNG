using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// Host key verification, covering the requirement that unknown and changed keys are presented and
/// that nothing is ever accepted silently.
/// </summary>
[TestFixture]
public class HostKeyGateTests
{
    private sealed class MemoryStore : IHostKeyStore
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

        public int SaveCount { get; private set; }

        public string? Find(string host, int port, string keyAlgorithm) =>
            _entries.TryGetValue(Key(host, port, keyAlgorithm), out string? value) ? value : null;

        public void Save(string host, int port, string keyAlgorithm, string fingerprint)
        {
            SaveCount++;
            _entries[Key(host, port, keyAlgorithm)] = fingerprint;
        }

        private static string Key(string host, int port, string algorithm) =>
            $"{host}|{port}|{algorithm}";
    }

    private sealed class RecordingVerifier(bool answer) : IHostKeyVerifier
    {
        public List<HostKeyPresentation> Asked { get; } = [];

        public bool Accept(HostKeyPresentation presentation)
        {
            Asked.Add(presentation);
            return answer;
        }
    }

    private const string Fingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
    private const string OtherFingerprint = "SHA256:9876543210zyxwvutsrqponmlkjihgfedcbaZYXWVUT";

    [Test]
    public void AnUnknownKeyIsPresentedForConfirmation()
    {
        MemoryStore store = new();
        RecordingVerifier verifier = new(answer: true);

        bool trusted = new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.True);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
            Assert.That(verifier.Asked[0].Fingerprint, Is.EqualTo(Fingerprint));
            Assert.That(verifier.Asked[0].PreviousFingerprint, Is.Null);
        });
    }

    [Test]
    public void AKnownKeyDoesNotPrompt()
    {
        MemoryStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingVerifier verifier = new(answer: false);

        bool trusted = new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.True);
            Assert.That(verifier.Asked, Is.Empty,
                "prompting for an already-accepted key trains users to click through the prompt");
        });
    }

    [Test]
    public void AChangedKeyIsReportedAsAChangeAndCarriesBothFingerprints()
    {
        MemoryStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingVerifier verifier = new(answer: true);

        bool trusted = new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-ed25519", OtherFingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.True);
            Assert.That(verifier.Asked, Has.Count.EqualTo(1));
            Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Changed));
            Assert.That(verifier.Asked[0].Fingerprint, Is.EqualTo(OtherFingerprint));
            Assert.That(verifier.Asked[0].PreviousFingerprint, Is.EqualTo(Fingerprint),
                "the user cannot judge a change without seeing what it changed from");
        });
    }

    [Test]
    public void RefusingAKeyStopsTheSessionAndRemembersNothing()
    {
        MemoryStore store = new();
        RecordingVerifier verifier = new(answer: false);

        bool trusted = new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.False);
            Assert.That(store.SaveCount, Is.Zero, "a refused key must not be recorded as accepted");
        });
    }

    [Test]
    public void AnAcceptedKeyIsRememberedSoTheNextConnectionIsSilent()
    {
        MemoryStore store = new();
        RecordingVerifier verifier = new(answer: true);
        HostKeyGate gate = new(store, verifier);

        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);
        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.That(verifier.Asked, Has.Count.EqualTo(1));
    }

    [Test]
    public void AcceptingAChangedKeyReplacesTheStoredOneRatherThanAddingToIt()
    {
        MemoryStore store = new();
        RecordingVerifier verifier = new(answer: true);
        HostKeyGate gate = new(store, verifier);

        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);
        gate.Evaluate("host.invalid", 22, "ssh-ed25519", OtherFingerprint);
        gate.Evaluate("host.invalid", 22, "ssh-ed25519", OtherFingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("host.invalid", 22, "ssh-ed25519"), Is.EqualTo(OtherFingerprint));
            Assert.That(verifier.Asked, Has.Count.EqualTo(2), "the third connection matched and must be silent");
        });
    }

    [Test]
    public void TheSameHostOnADifferentPortIsADifferentHost()
    {
        MemoryStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingVerifier verifier = new(answer: true);

        new HostKeyGate(store, verifier).Evaluate("host.invalid", 2222, "ssh-ed25519", OtherFingerprint);

        Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown),
            "a second daemon on another port is not the same server presenting a changed key");
    }

    [Test]
    public void ADifferentAlgorithmForTheSameHostIsNotAChange()
    {
        // A server offers several host keys; which one is negotiated depends on client preference.
        // Treating ed25519-then-rsa as a changed key would fire the alarm on ordinary behaviour.
        MemoryStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingVerifier verifier = new(answer: true);

        new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-rsa", OtherFingerprint);

        Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
    }

    [Test]
    public void WithNoVerifierNothingIsTrusted()
    {
        // The default when a session has no way to ask. Silent acceptance is the one outcome the
        // spec rules out, so refusal is the only safe fallback.
        MemoryStore store = new();

        bool trusted = new HostKeyGate(store, new DenyUnverifiedHostKeys())
            .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(trusted, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        });
    }
}

/// <summary>The on-disk half: round-tripping, replacement, and surviving a damaged file.</summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class FileHostKeyStoreTests
{
    private string _path = string.Empty;

    [SetUp]
    public void SetUp() =>
        _path = Path.Combine(Path.GetTempPath(), $"mrng-hostkeys-{Guid.NewGuid():N}.txt");

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Test]
    public void AnAcceptedKeySurvivesARestart()
    {
        new FileHostKeyStore(_path).Save("host.invalid", 22, "ssh-ed25519", "SHA256:aaa");

        Assert.That(new FileHostKeyStore(_path).Find("host.invalid", 22, "ssh-ed25519"),
            Is.EqualTo("SHA256:aaa"));
    }

    [Test]
    public void AnUnknownHostReadsAsNull()
    {
        Assert.That(new FileHostKeyStore(_path).Find("absent.invalid", 22, "ssh-ed25519"), Is.Null);
    }

    [Test]
    public void SavingTwiceReplacesRatherThanAppends()
    {
        FileHostKeyStore store = new(_path);
        store.Save("host.invalid", 22, "ssh-ed25519", "SHA256:aaa");
        store.Save("host.invalid", 22, "ssh-ed25519", "SHA256:bbb");

        int entries = Array.FindAll(File.ReadAllLines(_path), l => l.Contains("host.invalid", StringComparison.Ordinal)).Length;

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("host.invalid", 22, "ssh-ed25519"), Is.EqualTo("SHA256:bbb"));
            Assert.That(entries, Is.EqualTo(1),
                "a stale duplicate would make Find order-dependent and the old key would keep winning");
        });
    }

    [Test]
    public void HostNamesMatchCaseInsensitivelyButAlgorithmsDoNot()
    {
        FileHostKeyStore store = new(_path);
        store.Save("Host.Invalid", 22, "ssh-ed25519", "SHA256:aaa");

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("host.invalid", 22, "ssh-ed25519"), Is.EqualTo("SHA256:aaa"));
            Assert.That(store.Find("host.invalid", 22, "SSH-ED25519"), Is.Null);
        });
    }

    [Test]
    public void MalformedLinesAreSkippedRatherThanTakingTheFileDown()
    {
        File.WriteAllLines(_path,
        [
            "# a comment",
            "",
            "not-enough-fields",
            "host.invalid\tnot-a-port\tssh-ed25519\tSHA256:aaa",
            "good.invalid\t22\tssh-ed25519\tSHA256:bbb",
        ]);

        FileHostKeyStore store = new(_path);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("good.invalid", 22, "ssh-ed25519"), Is.EqualTo("SHA256:bbb"));
            Assert.That(store.Find("host.invalid", 22, "ssh-ed25519"), Is.Null);
        });
    }
}
