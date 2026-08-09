using System;

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
public sealed class HostKeyGate(IHostKeyStore store, IHostKeyVerifier verifier)
{
    private readonly IHostKeyStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IHostKeyVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

    /// <returns><c>true</c> if the session may proceed.</returns>
    public bool Evaluate(string host, int port, string keyAlgorithm, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentException.ThrowIfNullOrEmpty(keyAlgorithm);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);

        string? known = _store.Find(host, port, keyAlgorithm);

        // Already accepted: proceed without asking. Prompting every time trains users to click
        // through the prompt, which is what makes the changed-key case dangerous.
        if (string.Equals(known, fingerprint, StringComparison.Ordinal))
            return true;

        HostKeyPresentation presentation = new(
            host, port, keyAlgorithm, fingerprint, known,
            known is null ? HostKeyStatus.Unknown : HostKeyStatus.Changed);

        if (!_verifier.Accept(presentation))
            return false;

        _store.Save(host, port, keyAlgorithm, fingerprint);
        return true;
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
