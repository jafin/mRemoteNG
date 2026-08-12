using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.FileProtection;

namespace mRemoteNG.Security.SymmetricEncryption;

/// <summary>
/// AES-256-GCM under a connection file's own random key, with no key derivation at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file key is used directly.</b> It is 256 random bits from the system CSPRNG; there is
/// nothing to stretch, and running it through PBKDF2 would buy no strength while costing a
/// six-hundred-thousand-iteration derivation on every open and every save. The KDF in this format
/// applies to the recovery password only, which is the one secret a person chooses — see
/// <see cref="RecoveryPasswordKeyProtector"/> and <c>replace-default-connection-file-key</c> task 3.3.
/// </para>
/// <para>
/// <b>The <see cref="SecureString"/> arguments are ignored.</b> They exist because
/// <see cref="ICryptographyProvider"/> is shared with the password-keyed providers, and every call
/// site in the serializers passes <c>RootNodeInfo.PasswordString</c>. At this protection level that
/// string is not what the file is keyed on. Ignoring it is deliberate and is why the wire format
/// below is distinct: a file written here cannot be read back by the password-keyed provider, so a
/// caller that picked the wrong provider fails loudly instead of producing plausible bytes.
/// </para>
/// <para>
/// <see cref="KeyDerivationIterations"/> and <see cref="KeyDerivationPrf"/> are inert, as the
/// interface already allows for providers that derive no key. They are still recorded on the root
/// element by <c>XmlRootNodeSerializer</c>, where they describe nothing this provider does.
/// </para>
/// </remarks>
public sealed class PerFileKeyCryptographyProvider : IThreadSafeCryptographyProvider
{
    private const byte CurrentVersion = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MinimumLength = 1 + NonceLength + TagLength;

    private readonly byte[] _key;
    private readonly Encoding _encoding;

    public PerFileKeyCryptographyProvider(ConnectionFileKey fileKey)
        : this(fileKey, Encoding.UTF8)
    {
    }

    public PerFileKeyCryptographyProvider(ConnectionFileKey fileKey, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(encoding);

        // A copy, so that disposing the key — which the load path does as soon as it has built its
        // providers — does not pull the ground out from under an encrypt in progress.
        _key = fileKey.Bytes.ToArray();
        _encoding = encoding;
    }

    public int BlockSizeInBytes => 16;

    public BlockCipherEngines CipherEngine => BlockCipherEngines.AES;

    public BlockCipherModes CipherMode => BlockCipherModes.GCM;

    /// <summary>Inert. Nothing here derives a key.</summary>
    public int KeyDerivationIterations { get; set; }

    /// <summary>Inert. Nothing here derives a key.</summary>
    public HashAlgorithmName KeyDerivationPrf { get; set; }

    /// <param name="encryptionKey">Ignored — see the remarks on this class.</param>
    public string Encrypt(string plainText, SecureString encryptionKey)
    {
        if (string.IsNullOrEmpty(plainText))
            return "";

        byte[] secretMessage = _encoding.GetBytes(plainText);
        try
        {
            byte[] payload = new byte[1 + NonceLength + secretMessage.Length + TagLength];
            payload[0] = CurrentVersion;

            Span<byte> nonce = payload.AsSpan(1, NonceLength);
            RandomNumberGenerator.Fill(nonce);

            using AesGcm aes = new(_key, TagLength);
            aes.Encrypt(nonce,
                secretMessage,
                payload.AsSpan(1 + NonceLength, secretMessage.Length),
                payload.AsSpan(payload.Length - TagLength, TagLength),
                // The version byte is authenticated, so it cannot be rewritten to steer a future
                // build into reading this payload under different rules.
                payload.AsSpan(0, 1));

            return Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretMessage);
        }
    }

    /// <param name="decryptionKey">Ignored — see the remarks on this class.</param>
    /// <exception cref="EncryptionException">
    /// The payload is not what this provider wrote, or was altered. There is no path that returns
    /// something other than exactly what was encrypted.
    /// </exception>
    public string Decrypt(string cipherText, SecureString decryptionKey)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return "";

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException(Language.ErrorDecryptionFailed, ex);
        }

        if (payload.Length < MinimumLength || payload[0] != CurrentVersion)
            throw new EncryptionException(Language.ErrorDecryptionFailed);

        int messageLength = payload.Length - MinimumLength;
        byte[] plainText = new byte[messageLength];
        try
        {
            using AesGcm aes = new(_key, TagLength);
            aes.Decrypt(payload.AsSpan(1, NonceLength),
                payload.AsSpan(1 + NonceLength, messageLength),
                payload.AsSpan(payload.Length - TagLength, TagLength),
                plainText,
                payload.AsSpan(0, 1));

            return _encoding.GetString(plainText);
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException(Language.ErrorDecryptionFailed, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainText);
        }
    }
}
