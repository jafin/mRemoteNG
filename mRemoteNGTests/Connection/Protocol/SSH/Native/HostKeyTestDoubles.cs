using System;
using System.Collections.Generic;
using System.Threading;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// A host key store that keeps nothing on disk, so a test can state what the user has already
/// accepted without owning a file.
/// </summary>
public sealed class MemoryHostKeyStore : IHostKeyStore
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public int SaveCount { get; private set; }

    /// <summary>Runs on every lookup, on the calling thread. The seam concurrency tests need.</summary>
    public Action? OnFind { get; set; }

    public string? Find(string host, int port, string keyAlgorithm)
    {
        OnFind?.Invoke();

        lock (_gate)
        {
            return _entries.TryGetValue(Key(host, port, keyAlgorithm), out string? value) ? value : null;
        }
    }

    public void Save(string host, int port, string keyAlgorithm, string fingerprint)
    {
        lock (_gate)
        {
            SaveCount++;
            _entries[Key(host, port, keyAlgorithm)] = fingerprint;
        }
    }

    /// <summary>
    /// Matches <c>FileHostKeyStore</c>: host names are case-insensitive, the algorithm is a protocol
    /// identifier and is not.
    /// </summary>
    /// <remarks>
    /// A double that is stricter than the real store is not a safe simplification. It would report a
    /// prompt where production is silent, so a test could be made to pass by "fixing" behaviour that
    /// was never broken — or, worse, a genuine casing bug could hide behind a double that never
    /// matched anything anyway.
    /// </remarks>
    private static string Key(string host, int port, string algorithm) =>
        $"{host.ToUpperInvariant()}|{port}|{algorithm}";
}

/// <summary>
/// Answers every prompt the same way and remembers what it was shown, so a test can assert both
/// how many questions were asked and what the user would have seen.
/// </summary>
public sealed class RecordingHostKeyVerifier(bool answer) : IHostKeyVerifier
{
    private readonly Lock _gate = new();

    public List<HostKeyPresentation> Asked { get; } = [];

    /// <summary>Runs inside the answer, on the asking thread. Lets a test hold a decision open.</summary>
    public Action? WhileAsking { get; set; }

    public bool Accept(HostKeyPresentation presentation)
    {
        lock (_gate)
        {
            Asked.Add(presentation);
        }

        WhileAsking?.Invoke();
        return answer;
    }
}

/// <summary>Fingerprints in the shape SSH.NET hands over, for tests that need two that differ.</summary>
public static class HostKeyFingerprints
{
    public const string First = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
    public const string Second = "SHA256:9876543210zyxwvutsrqponmlkjihgfedcbaZYXWVUT";
}
