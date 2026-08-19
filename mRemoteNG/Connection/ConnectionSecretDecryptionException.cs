using System;

namespace mRemoteNG.Connection;

/// <summary>
/// A stored secret could not be decrypted when something asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Thrown rather than answered with an empty string, and that choice is the whole point of the
/// type. Several callers read an empty secret as "no password configured" and fall through to the
/// configured default password, so a failure that returned empty would send the wrong credentials
/// to a host instead of reporting anything at all.
/// </para>
/// <para>
/// It carries the connection's name because the failure is now met one connection at a time. A
/// wrong file key is still caught once, while the file is being opened; this is what is left over
/// for the single corrupt attribute in an otherwise good file, and a message that did not say which
/// connection it was about would be useless.
/// </para>
/// </remarks>
public class ConnectionSecretDecryptionException : Exception
{
    public string ConnectionName { get; } = string.Empty;

    public string SecretName { get; } = string.Empty;

    public ConnectionSecretDecryptionException()
    {
    }

    public ConnectionSecretDecryptionException(string message) : base(message)
    {
    }

    public ConnectionSecretDecryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConnectionSecretDecryptionException(string connectionName, string secretName, Exception innerException)
        : base(BuildMessage(connectionName, secretName), innerException)
    {
        ConnectionName = connectionName;
        SecretName = secretName;
    }

    private static string BuildMessage(string connectionName, string secretName)
    {
        string named = string.IsNullOrWhiteSpace(connectionName) ? "an unnamed connection" : $"'{connectionName}'";
        return $"The stored {secretName} for {named} could not be decrypted. The rest of the connection " +
               "file is unaffected; re-enter this one secret to repair it.";
    }
}
