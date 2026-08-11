using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
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
    private const string Fingerprint = HostKeyFingerprints.First;
    private const string OtherFingerprint = HostKeyFingerprints.Second;

    [Test]
    public void AnUnknownKeyIsPresentedForConfirmation()
    {
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: true);

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
        MemoryHostKeyStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingHostKeyVerifier verifier = new(answer: false);

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
        MemoryHostKeyStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingHostKeyVerifier verifier = new(answer: true);

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
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);

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
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: true);
        HostKeyGate gate = new(store, verifier);

        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);
        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.That(verifier.Asked, Has.Count.EqualTo(1));
    }

    [Test]
    public void AcceptingAChangedKeyReplacesTheStoredOneRatherThanAddingToIt()
    {
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: true);
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
        MemoryHostKeyStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingHostKeyVerifier verifier = new(answer: true);

        new HostKeyGate(store, verifier).Evaluate("host.invalid", 2222, "ssh-ed25519", OtherFingerprint);

        Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown),
            "a second daemon on another port is not the same server presenting a changed key");
    }

    [Test]
    public void ADifferentAlgorithmForTheSameHostIsNotAChange()
    {
        // A server offers several host keys; which one is negotiated depends on client preference.
        // Treating ed25519-then-rsa as a changed key would fire the alarm on ordinary behaviour.
        MemoryHostKeyStore store = new();
        store.Save("host.invalid", 22, "ssh-ed25519", Fingerprint);
        RecordingHostKeyVerifier verifier = new(answer: true);

        new HostKeyGate(store, verifier).Evaluate("host.invalid", 22, "ssh-rsa", OtherFingerprint);

        Assert.That(verifier.Asked[0].Status, Is.EqualTo(HostKeyStatus.Unknown));
    }

    [Test]
    public void TwoConnectionsToOneUnknownEndpointAskOnceAndShareTheAnswer()
    {
        // A session and the file manager opening together. Each owns its own gate — the verifier
        // has to reach the window that is connecting — so the serialization has to hold across
        // gates rather than within one, which is why the lock is a separate object.
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: true);
        HostKeyDecisionLock decisions = new();

        using CountdownEvent bothArrived = new(2);
        HashSet<int> callers = [];
        Lock callersGate = new();

        store.OnFind = () =>
        {
            lock (callersGate)
            {
                if (callers.Add(Environment.CurrentManagedThreadId))
                    bothArrived.Signal();
            }
        };

        // The decision is held open until the second caller has entered the gate, so the race is
        // forced rather than hoped for: the second caller cannot reach the verifier without the
        // lock the first one is holding while this runs.
        bool raced = false;
        verifier.WhileAsking = () => raced = bothArrived.Wait(TimeSpan.FromSeconds(30));

        bool[] trusted = new bool[2];
        Task[] connections =
        [
            Task.Run(() => trusted[0] = new HostKeyGate(store, verifier, decisions)
                .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint)),
            Task.Run(() => trusted[1] = new HostKeyGate(store, verifier, decisions)
                .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint)),
        ];

        bool finished = Task.WaitAll(connections, TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.True, "a connection never returned; the endpoint lock deadlocked");
            Assert.That(raced, Is.True, "both connections must have been in the gate at once");
            Assert.That(verifier.Asked, Has.Count.EqualTo(1),
                "one endpoint asked about twice is two chances to answer it differently");
            Assert.That(trusted, Is.All.True, "both connections take the answer the user gave");
            Assert.That(store.SaveCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoConnectionsToOneUnknownEndpointShareARefusalToo()
    {
        // The case the store cannot cover. A refused key is deliberately never written, so a caller
        // queued behind a refusal finds an unknown endpoint and would ask again — leaving "asked
        // once" true only when the answer happened to be yes. A user who says no to a changed key
        // on a host two features are opening at once must not be asked twice.
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);
        HostKeyDecisionLock decisions = new();

        using CountdownEvent bothArrived = new(2);
        HashSet<int> callers = [];
        Lock callersGate = new();

        store.OnFind = () =>
        {
            lock (callersGate)
            {
                if (callers.Add(Environment.CurrentManagedThreadId))
                    bothArrived.Signal();
            }
        };

        bool raced = false;
        verifier.WhileAsking = () => raced = bothArrived.Wait(TimeSpan.FromSeconds(30));

        bool[] trusted = [true, true];
        Task[] connections =
        [
            Task.Run(() => trusted[0] = new HostKeyGate(store, verifier, decisions)
                .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint)),
            Task.Run(() => trusted[1] = new HostKeyGate(store, verifier, decisions)
                .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint)),
        ];

        bool finished = Task.WaitAll(connections, TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.True, "a connection never returned; the endpoint lock deadlocked");
            Assert.That(raced, Is.True, "both connections must have been in the gate at once");
            Assert.That(verifier.Asked, Has.Count.EqualTo(1), "one endpoint, one question — refused or not");
            Assert.That(trusted, Is.All.False, "both connections take the refusal");
            Assert.That(store.SaveCount, Is.Zero, "a refused key must not be recorded as accepted");
        });
    }

    [Test]
    public void ARefusalIsNotRememberedBeyondTheConnectionsWaitingOnIt()
    {
        // The other half of sharing a refusal: it must not become a denial the user cannot undo.
        // A connection opened afterwards is a fresh attempt and is entitled to its own question.
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);
        HostKeyDecisionLock decisions = new();
        HostKeyGate gate = new(store, verifier, decisions);

        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);
        gate.Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.That(verifier.Asked, Has.Count.EqualTo(2),
            "a refusal shared with a later, unrelated connection would deny it silently");
    }

    [Test]
    public void ASharedAnswerAppliesOnlyToTheKeyItWasGivenFor()
    {
        // The answer is fingerprint-matched. If the host offers a different key to the second
        // connection, that is a different question and the first answer says nothing about it.
        MemoryHostKeyStore store = new();
        RecordingHostKeyVerifier verifier = new(answer: false);
        HostKeyDecisionLock decisions = new();

        using HostKeyDecision decision = decisions.Acquire("host.invalid", 22, "ssh-ed25519");
        decision.Publish(Fingerprint, accepted: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.TryGetSharedAnswer(OtherFingerprint, out _), Is.False,
                "a refusal of one key must not answer for another");
            Assert.That(decision.TryGetSharedAnswer(Fingerprint, out bool sameKey), Is.True);
            Assert.That(sameKey, Is.False, "the answer carried is the one that was given");
        });

        // Unused here; the gate's own path is covered by the concurrent tests above.
        Assert.That(verifier.Asked, Is.Empty);
    }

    [Test]
    public void ConnectionsToDifferentEndpointsDoNotWaitOnEachOther()
    {
        // Per endpoint, not global. A prompt open for one host must not stall a connection to
        // another, and the store's key is the honest unit to hold.
        MemoryHostKeyStore store = new();
        HostKeyDecisionLock decisions = new();

        using ManualResetEventSlim secondFinished = new();

        // Separate verifiers, because only one of the two callers is meant to be held: sharing one
        // and branching on how many questions it had been asked would decide which caller waits by
        // whichever got there first.
        RecordingHostKeyVerifier held = new(answer: true)
        {
            WhileAsking = () => secondFinished.Wait(TimeSpan.FromSeconds(30))
        };

        RecordingHostKeyVerifier prompt = new(answer: true);

        Task first = Task.Run(() => new HostKeyGate(store, held, decisions)
            .Evaluate("host.invalid", 22, "ssh-ed25519", Fingerprint));

        Task second = Task.Run(() =>
        {
            new HostKeyGate(store, prompt, decisions)
                .Evaluate("other.invalid", 22, "ssh-ed25519", Fingerprint);
            secondFinished.Set();
        });

        Assert.That(Task.WaitAll([first, second], TimeSpan.FromSeconds(60)), Is.True,
            "a decision about one endpoint blocked a connection to a different one");
    }

    [Test]
    public void TheStoreDoubleMatchesHostNamesTheWayTheRealStoreDoes()
    {
        // Guards the double, not the gate. FileHostKeyStore compares host names case-insensitively
        // and algorithms ordinally; a double that compared both ordinally would report a prompt
        // where production is silent, and every test built on it would be measuring the double.
        MemoryHostKeyStore store = new();
        store.Save("Host.Invalid", 22, "ssh-ed25519", Fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find("host.invalid", 22, "ssh-ed25519"), Is.EqualTo(Fingerprint));
            Assert.That(store.Find("HOST.INVALID", 22, "ssh-ed25519"), Is.EqualTo(Fingerprint));
            Assert.That(store.Find("host.invalid", 22, "SSH-ED25519"), Is.Null,
                "the algorithm is a protocol identifier, not a name");
            Assert.That(store.Find("host.invalid", 2222, "ssh-ed25519"), Is.Null);
        });
    }

    [Test]
    public void WithNoVerifierNothingIsTrusted()
    {
        // The default when a session has no way to ask. Silent acceptance is the one outcome the
        // spec rules out, so refusal is the only safe fallback.
        MemoryHostKeyStore store = new();

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
