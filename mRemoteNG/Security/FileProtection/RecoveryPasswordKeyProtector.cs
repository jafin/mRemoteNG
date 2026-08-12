using System;
using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// Wraps the file key with a key stretched from a recovery password.
/// </summary>
/// <remarks>
/// <para>
/// This is the protector that travels. <c>FileBackupCreator</c> copies the encrypted file and
/// re-encrypts nothing, so whatever protects the live file protects every backup of it — and a
/// machine-bound protector alone would mean a backup restored anywhere else is worthless, silently,
/// with no signal until the day it is needed. See design.md, "Why this changed".
/// </para>
/// <para>
/// The wrapped blob carries its own KDF parameters rather than reading them off the root element.
/// The root's <c>KdfIterations</c> and <c>KdfPrf</c> describe how the file's <i>contents</i> are
/// keyed, and at this protection level the contents are not derived from a password at all — the file
/// key is used directly. Two different derivations sharing one pair of attributes is how a change to
/// one silently breaks the other.
/// </para>
/// </remarks>
public static class RecoveryPasswordKeyProtector
{
    /// <summary>
    /// The iteration count used when a caller states none.
    /// </summary>
    /// <remarks>
    /// OWASP's figure for PBKDF2-HMAC-SHA256, and the same count the connection file already uses for
    /// its content key. A recovery password is typed rarely, so there is no interactive cost to
    /// weigh against it.
    /// </remarks>
    public const int DefaultIterations = 600_000;

    private const byte CurrentVersion = 1;
    private const byte PrfSha256 = 1;
    private const byte PrfSha512 = 2;

    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 1 + 1 + 4 + SaltLength;
    private const int BlobLength = HeaderLength + NonceLength + ConnectionFileKey.SizeInBytes + TagLength;

    /// <summary>
    /// The functions a recovery protector may be written with.
    /// </summary>
    /// <remarks>
    /// SHA-1 is deliberately not among them. It is the default elsewhere in the file format because
    /// every file written in the last fifteen years used it and upstream mRemoteNG still does, so
    /// changing it would strand files. A recovery protector has no such history to preserve: it is
    /// written for the first time here.
    /// </remarks>
    public static bool IsSupportedPrf(HashAlgorithmName function) =>
        function == HashAlgorithmName.SHA256 || function == HashAlgorithmName.SHA512;

    public static string Wrap(ConnectionFileKey fileKey, SecureString recoveryPassword,
                              int iterations = DefaultIterations, HashAlgorithmName? prf = null)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        HashAlgorithmName function = prf ?? KeyDerivationPrf.Hardened;
        if (!IsSupportedPrf(function))
            throw new ArgumentOutOfRangeException(nameof(prf), function,
                "A recovery protector must be derived with SHA-256 or SHA-512.");

        if (recoveryPassword.Length == 0)
            throw new ArgumentException("A recovery password is required.", nameof(recoveryPassword));

        byte[] blob = new byte[BlobLength];
        blob[0] = CurrentVersion;
        blob[1] = ToPrfId(function);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(2, 4), iterations);

        Span<byte> salt = blob.AsSpan(6, SaltLength);
        RandomNumberGenerator.Fill(salt);

        Span<byte> nonce = blob.AsSpan(HeaderLength, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        byte[] keyEncryptionKey = DeriveKeyEncryptionKey(recoveryPassword, salt, iterations, function);
        try
        {
            using AesGcm aes = new(keyEncryptionKey, TagLength);
            aes.Encrypt(nonce,
                fileKey.Bytes,
                blob.AsSpan(HeaderLength + NonceLength, ConnectionFileKey.SizeInBytes),
                blob.AsSpan(BlobLength - TagLength, TagLength),
                // The parameters are authenticated, so an attacker cannot drop the iteration count to
                // something brute-forceable and have the file still open.
                blob.AsSpan(0, HeaderLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
        }

        return Convert.ToBase64String(blob);
    }

    /// <exception cref="KeyProtectionException">
    /// The password is wrong, or the blob is unreadable or was written by a build that knows a format
    /// this one does not. Never returns a wrong key: AES-GCM authenticates, so a wrong password fails
    /// the tag rather than producing plausible bytes — which is what separates this from the legacy
    /// AES-CBC path, where a wrong key yields valid padding roughly once in 256 attempts.
    /// </exception>
    public static ConnectionFileKey Unwrap(string? wrappedKey, SecureString recoveryPassword)
    {
        ArgumentNullException.ThrowIfNull(recoveryPassword);

        if (string.IsNullOrWhiteSpace(wrappedKey))
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "This connection file carries no recovery-password protector.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(wrappedKey);
        }
        catch (FormatException ex)
        {
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "The recovery protector on this connection file is not readable.", ex);
        }

        if (blob.Length != BlobLength || blob[0] != CurrentVersion)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "The recovery protector on this connection file is in a format this version does not " +
                "recognise, so it was written by a newer one.");

        HashAlgorithmName function;
        try
        {
            function = FromPrfId(blob[1]);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "The recovery protector names a key derivation function this version does not know.", ex);
        }

        int iterations = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(2, 4));
        if (iterations < 1000)
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "The recovery protector records an iteration count too low to have been written by " +
                "this application.");

        byte[] keyEncryptionKey = DeriveKeyEncryptionKey(recoveryPassword, blob.AsSpan(6, SaltLength),
                                                         iterations, function);
        byte[] fileKey = new byte[ConnectionFileKey.SizeInBytes];
        try
        {
            using AesGcm aes = new(keyEncryptionKey, TagLength);
            aes.Decrypt(blob.AsSpan(HeaderLength, NonceLength),
                blob.AsSpan(HeaderLength + NonceLength, ConnectionFileKey.SizeInBytes),
                blob.AsSpan(BlobLength - TagLength, TagLength),
                fileKey,
                blob.AsSpan(0, HeaderLength));

            return ConnectionFileKey.FromBytes(fileKey);
        }
        catch (CryptographicException ex)
        {
            throw new KeyProtectionException(KeyProtector.RecoveryPassword,
                "The recovery password did not open this connection file.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    private static byte[] DeriveKeyEncryptionKey(SecureString recoveryPassword, ReadOnlySpan<byte> salt,
                                                 int iterations, HashAlgorithmName function)
    {
        Pkcs5S2KeyGenerator generator = new(ConnectionFileKey.SizeInBytes * 8, iterations, function);
        return generator.DeriveKey(recoveryPassword.ConvertToUnsecureString(), salt.ToArray());
    }

    private static byte ToPrfId(HashAlgorithmName function) =>
        function == HashAlgorithmName.SHA512 ? PrfSha512 : PrfSha256;

    private static HashAlgorithmName FromPrfId(byte id) => id switch
    {
        PrfSha256 => HashAlgorithmName.SHA256,
        PrfSha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
    };
}
