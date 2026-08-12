using System;
using System.Security.Cryptography;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// The random key a connection file's contents are encrypted with, held only for as long as it is
/// needed.
/// </summary>
/// <remarks>
/// <para>
/// The key is generated once per file and never derived from anything. It is the value the two
/// protectors wrap: the machine protector and the recovery-password protector each hold a copy, and
/// either unwraps this.
/// </para>
/// <para>
/// A type rather than a bare <c>byte[]</c> so that the one thing that must happen to key material —
/// being zeroed when it goes out of use — has somewhere to live, and so that a caller cannot hand a
/// wrong-sized array to a protector and find out at encryption time.
/// </para>
/// </remarks>
public sealed class ConnectionFileKey : IDisposable
{
    /// <summary>AES-256, matching what the file's contents are encrypted with.</summary>
    public const int SizeInBytes = 32;

    private readonly byte[] _key;
    private bool _disposed;

    private ConnectionFileKey(byte[] key) => _key = key;

    /// <summary>A new key from the system CSPRNG.</summary>
    public static ConnectionFileKey Generate()
    {
        byte[] key = new byte[SizeInBytes];
        RandomNumberGenerator.Fill(key);
        return new ConnectionFileKey(key);
    }

    /// <summary>
    /// Adopts key material a protector has just unwrapped. The bytes are copied, so the caller
    /// remains free to zero its own buffer.
    /// </summary>
    public static ConnectionFileKey FromBytes(ReadOnlySpan<byte> key)
    {
        if (key.Length != SizeInBytes)
            throw new ArgumentException(
                $"A connection file key is {SizeInBytes} bytes; got {key.Length}.", nameof(key));

        return new ConnectionFileKey(key.ToArray());
    }

    /// <summary>
    /// The key material. Valid only until this instance is disposed, which is why it is a span:
    /// nothing can hold it past the point where the bytes are zeroed.
    /// </summary>
    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
