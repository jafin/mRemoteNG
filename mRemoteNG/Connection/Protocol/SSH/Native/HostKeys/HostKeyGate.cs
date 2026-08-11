using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;

/// <summary>How a presented host key relates to what was accepted before.</summary>
public enum HostKeyStatus
{
    /// <summary>Never seen. The user is trusting the host on first sight.</summary>
    Unknown,

    /// <summary>Seen before, but the host is now presenting a different key.</summary>
    Changed
}

/// <param name="PreviousFingerprint">
/// What was accepted last time; null when <see cref="HostKeyStatus.Unknown"/>.
/// </param>
public sealed record HostKeyPresentation(
    string Host,
    int Port,
    string KeyAlgorithm,
    string Fingerprint,
    string? PreviousFingerprint,
    HostKeyStatus Status);

/// <summary>Where accepted host keys are remembered.</summary>
public interface IHostKeyStore
{
    /// <summary>The fingerprint previously accepted for this host, port and algorithm, if any.</summary>
    string? Find(string host, int port, string keyAlgorithm);

    void Save(string host, int port, string keyAlgorithm, string fingerprint);
}

/// <summary>Asks the user whether to trust a key. Implementations may block.</summary>
public interface IHostKeyVerifier
{
    bool Accept(HostKeyPresentation presentation);
}

/// <summary>
/// Decides whether a session may proceed with the host key it was offered.
/// </summary>
/// <remarks>
/// <para>
/// PuTTY owned this; hosting the transport in-process means inheriting it. The one behaviour that
/// is not permitted is silent acceptance — that would quietly remove a protection users have today,
/// and the removal would be invisible precisely because nothing would appear on screen.
/// </para>
/// <para>
/// Deliberately free of SSH.NET and of WinForms. The interesting cases — a changed key, a refusal,
/// a key accepted once and not re-prompted — are then testable without a server or a dialog, which
/// matters because a prompt that is wrong in the changed-key case is a security defect rather than
/// an inconvenience.
/// </para>
/// </remarks>
/// <param name="decisions">
/// Serializes deciding about one endpoint. Defaults to the process-wide instance, which is what
/// makes two connections opened at once cost one prompt rather than two; pass a private one to
/// isolate a test.
/// </param>
public sealed class HostKeyGate(
    IHostKeyStore store, IHostKeyVerifier verifier, HostKeyDecisionLock? decisions = null)
{
    private readonly IHostKeyStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IHostKeyVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    private readonly HostKeyDecisionLock _decisions = decisions ?? HostKeyDecisionLock.Shared;

    /// <returns><c>true</c> if the session may proceed.</returns>
    public bool Evaluate(string host, int port, string keyAlgorithm, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentException.ThrowIfNullOrEmpty(keyAlgorithm);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);

        // Already accepted: proceed without asking, and without touching the lock. Prompting every
        // time trains users to click through the prompt, which is what makes the changed-key case
        // dangerous. Staying clear of the lock matters too — a caller on the UI thread must not
        // wait on a decision another thread is making, and this is the case that would.
        if (IsAlreadyAccepted(host, port, keyAlgorithm, fingerprint))
            return true;

        using IDisposable decision = _decisions.Acquire(host, port, keyAlgorithm);

        // Re-read under the lock. Another connection to this endpoint may have asked while this one
        // waited, and taking its answer is the whole point: one question, one answer, both
        // connections. Without this the loser of the race asks the user the same thing again.
        if (IsAlreadyAccepted(host, port, keyAlgorithm, fingerprint))
            return true;

        string? known = _store.Find(host, port, keyAlgorithm);

        HostKeyPresentation presentation = new(
            host, port, keyAlgorithm, fingerprint, known,
            known is null ? HostKeyStatus.Unknown : HostKeyStatus.Changed);

        if (!_verifier.Accept(presentation))
            return false;

        _store.Save(host, port, keyAlgorithm, fingerprint);
        return true;
    }

    private bool IsAlreadyAccepted(string host, int port, string keyAlgorithm, string fingerprint) =>
        string.Equals(_store.Find(host, port, keyAlgorithm), fingerprint, StringComparison.Ordinal);
}

/// <summary>
/// Holds an endpoint while it is being decided about, so concurrent connections to it ask once.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="HostKeyGate"/> because each window owns its own gate — the verifier has
/// to reach a live UI thread — while the decision has to be serialized across all of them. A field
/// on the gate would serialize nothing between the file manager and a session.
/// </para>
/// <para>
/// The wait is bounded. Every consumer connects off the UI thread, which is what keeps a caller
/// from waiting here while holding the thread a dialog has to be shown on — but a future one that
/// forgets would otherwise deadlock rather than merely stall. On expiry the caller falls through
/// and asks its own question. Two prompts is the outcome this class exists to avoid, but it is not
/// a safety failure, and a deadlocked application would be worse than being asked twice.
/// </para>
/// </remarks>
public sealed class HostKeyDecisionLock
{
    private static readonly TimeSpan MaximumWait = TimeSpan.FromMinutes(2);

    /// <summary>The one every gate uses unless told otherwise.</summary>
    public static HostKeyDecisionLock Shared { get; } = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _table = new();

    /// <summary>Holds the endpoint until the returned handle is disposed.</summary>
    public IDisposable Acquire(string host, int port, string keyAlgorithm)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentException.ThrowIfNullOrEmpty(keyAlgorithm);

        // The store's key, so the unit that is held is the unit that is remembered: a prompt for
        // one endpoint must not block a connection to a different port on the same machine.
        string key = string.Create(CultureInfo.InvariantCulture,
            $"{host.ToUpperInvariant()}|{port}|{keyAlgorithm}");

        Entry entry;
        lock (_table)
        {
            if (!_entries.TryGetValue(key, out Entry? existing))
            {
                existing = new Entry();
                _entries[key] = existing;
            }

            // Claimed before waiting, so the entry cannot be removed and replaced by a releasing
            // holder while this caller is queued on the semaphore it is queued on.
            existing.Waiters++;
            entry = existing;
        }

        // A caller that timed out proceeds unserialized rather than failing the connection, and must
        // not release a semaphore it never took.
        bool held = entry.Gate.Wait(MaximumWait);
        return new Release(this, key, entry, held);
    }

    private void ReleaseEntry(string key, Entry entry, bool held)
    {
        if (held)
            entry.Gate.Release();

        lock (_table)
        {
            if (--entry.Waiters > 0)
                return;

            if (_entries.TryGetValue(key, out Entry? current) && current == entry)
                _entries.Remove(key);

            entry.Gate.Dispose();
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Everyone holding or queued for <see cref="Gate"/>, guarded by the table lock.</summary>
        public int Waiters;
    }

    private sealed class Release(HostKeyDecisionLock owner, string key, Entry entry, bool held) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            owner.ReleaseEntry(key, entry, held);
        }
    }
}

/// <summary>
/// Refuses everything. The default when no verifier was supplied: a session that cannot ask must
/// not decide on the user's behalf, and refusing is the only answer that cannot lose them anything.
/// </summary>
public sealed class DenyUnverifiedHostKeys : IHostKeyVerifier
{
    public bool Accept(HostKeyPresentation presentation) => false;
}
