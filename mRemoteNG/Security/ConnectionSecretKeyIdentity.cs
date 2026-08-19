using System;
using System.Security.Cryptography;
using System.Text;

namespace mRemoteNG.Security;

/// <summary>
/// Which key, and which parameters, a connection file's secrets are encrypted under.
/// </summary>
/// <remarks>
/// <para>
/// Exists for one decision: whether a save may write back a ciphertext it never decrypted. That is
/// safe only when the bytes it writes would still be readable by the key the file ends up declaring,
/// and it is catastrophic when they would not — a file whose contents are encrypted under two
/// different keys, half of which nothing can ever decrypt again.
/// </para>
/// <para>
/// <b>Compared by value, and deliberately not by anyone remembering to invalidate it.</b> The
/// alternative was a flag set by every path that changes a key — a rekey, a master-password change,
/// hardening a classic store, an export at a different level — and the cost of one of them being
/// forgotten, or of a new one being added later by somebody who never read this, is exactly the
/// destructive outcome above. Here a key that changed produces a different identity on its own, and
/// the pass-through simply stops applying. Being wrong costs a decryption that was not needed; the
/// other direction costs the file.
/// </para>
/// <para>
/// The password is held as a fingerprint rather than as itself, so that an identity captured when a
/// store was opened cannot keep a superseded master password alive for the rest of the session.
/// </para>
/// </remarks>
public sealed class ConnectionSecretKeyIdentity : IEquatable<ConnectionSecretKeyIdentity>
{
    private readonly string _providerKind;
    private readonly BlockCipherEngines _engine;
    private readonly BlockCipherModes _mode;
    private readonly int _iterations;
    private readonly string _keyDerivationFunction;
    private readonly object? _fileKey;
    private readonly string _passwordFingerprint;

    private ConnectionSecretKeyIdentity(string providerKind, BlockCipherEngines engine, BlockCipherModes mode,
                                        int iterations, string keyDerivationFunction, object? fileKey,
                                        string passwordFingerprint)
    {
        _providerKind = providerKind;
        _engine = engine;
        _mode = mode;
        _iterations = iterations;
        _keyDerivationFunction = keyDerivationFunction;
        _fileKey = fileKey;
        _passwordFingerprint = passwordFingerprint;
    }

    /// <param name="fileKey">
    /// The store's own key, when it has one. Compared by reference: a rekey builds a new key object
    /// and disposes the old, so two different keys can never compare equal however their bytes are
    /// arranged.
    /// </param>
    /// <param name="password">
    /// What the store derives its key from, when it has no key of its own. Ignored entirely when
    /// <paramref name="fileKey"/> is supplied, because nothing is derived from it in that case.
    /// </param>
    public static ConnectionSecretKeyIdentity For(ICryptographyProvider cryptographyProvider,
                                                  object? fileKey,
                                                  string? password)
    {
        ArgumentNullException.ThrowIfNull(cryptographyProvider);

        return new ConnectionSecretKeyIdentity(
            cryptographyProvider.GetType().FullName ?? cryptographyProvider.GetType().Name,
            cryptographyProvider.CipherEngine,
            cryptographyProvider.CipherMode,
            cryptographyProvider.KeyDerivationIterations,
            cryptographyProvider.KeyDerivationPrf.Name ?? string.Empty,
            fileKey,
            fileKey is not null ? string.Empty : Fingerprint(password));
    }

    /// <summary>
    /// A random key, made once per run, that the password fingerprints are taken under.
    /// </summary>
    /// <remarks>
    /// Identities are only ever compared with other identities built in the same process, so a
    /// per-process key costs nothing and removes what a plain digest would be worth to somebody
    /// reading this process's memory: an unsalted SHA-256 of a master password can be ground
    /// against a wordlist, and a keyed one cannot.
    /// </remarks>
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);

    private static string Fingerprint(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "empty";

        byte[] bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(FingerprintKey, bytes));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public bool Equals(ConnectionSecretKeyIdentity? other)
    {
        return other is not null &&
               string.Equals(_providerKind, other._providerKind, StringComparison.Ordinal) &&
               _engine == other._engine &&
               _mode == other._mode &&
               _iterations == other._iterations &&
               string.Equals(_keyDerivationFunction, other._keyDerivationFunction, StringComparison.Ordinal) &&
               ReferenceEquals(_fileKey, other._fileKey) &&
               string.Equals(_passwordFingerprint, other._passwordFingerprint, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as ConnectionSecretKeyIdentity);

    public override int GetHashCode() =>
        HashCode.Combine(_providerKind, _engine, _mode, _iterations, _keyDerivationFunction, _passwordFingerprint);
}
