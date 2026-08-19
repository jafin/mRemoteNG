using System;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Security;

namespace mRemoteNG.Connection;

/// <summary>
/// A secret a connection record holds as ciphertext, not yet decrypted because nothing has asked
/// for it.
/// </summary>
/// <remarks>
/// <para>
/// Opening a file of two hundred connections used to put two hundred passwords into the process, for
/// the lifetime of the session, to serve the handful the user actually opens. This is what stands in
/// their place until one is wanted.
/// </para>
/// <para>
/// It carries the key it was read under so that a save can tell whether writing these same bytes
/// back is still correct. See <see cref="ConnectionSecretKeyIdentity"/> for why that question is
/// answered by comparing keys rather than by trusting each save path to declare itself.
/// </para>
/// </remarks>
public sealed class PendingConnectionSecret
{
    private readonly Func<string, string> _decrypt;
    private readonly ConnectionSecretKeyIdentity _key;
    private bool _failureReported;

    public PendingConnectionSecret(string cipherText, ConnectionSecretKeyIdentity key, Func<string, string> decrypt)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(decrypt);
        CipherText = cipherText;
        _key = key;
        _decrypt = decrypt;
    }

    /// <summary>
    /// The stored bytes, exactly as the file held them.
    /// </summary>
    public string CipherText { get; }

    /// <summary>
    /// Whether these bytes are still readable by the given key, and so may be written back untouched.
    /// </summary>
    public bool WasWrittenUnder(ConnectionSecretKeyIdentity? key) => key is not null && _key.Equals(key);

    /// <summary>
    /// Decrypts, or reports the failure against the connection it belongs to and throws.
    /// </summary>
    /// <remarks>
    /// The report happens once per secret, not once per attempt. A caller that meets the exception
    /// and carries on — the property grid re-reads a displayed connection constantly — would
    /// otherwise produce a wall of identical messages about one broken field.
    /// </remarks>
    /// <exception cref="ConnectionSecretDecryptionException">The stored bytes did not decrypt.</exception>
    public string Resolve(string connectionName, string secretName)
    {
        try
        {
            return _decrypt(CipherText);
        }
        catch (Exception ex) when (ex is not ConnectionSecretDecryptionException)
        {
            ConnectionSecretDecryptionException failure = new(connectionName, secretName, ex);

            if (!_failureReported)
            {
                _failureReported = true;
                Runtime.MessageCollector?.AddExceptionMessage(failure.Message, ex, MessageClass.ErrorMsg,
                    logOnly: false);
            }

            throw failure;
        }
    }
}
