using System.Security;
using System.Security.Cryptography;

namespace mRemoteNG.Security;

public interface ICryptographyProvider
{
    int BlockSizeInBytes { get; }

    BlockCipherEngines CipherEngine { get; }

    BlockCipherModes CipherMode { get; }

    int KeyDerivationIterations { get; set; }

    /// <summary>
    /// The PBKDF2 pseudo-random function. Meaningless to providers that derive no key, which is why
    /// they may ignore it, exactly as they ignore <see cref="KeyDerivationIterations"/>.
    /// </summary>
    HashAlgorithmName KeyDerivationPrf { get; set; }

    /// <summary>
    /// Whether a failed decryption means the ciphertext is <i>wrong</i>, rather than merely
    /// unrecognised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what separates a provider whose failure a caller may ignore from one whose failure is
    /// evidence. An unauthenticated cipher cannot tell "somebody altered this" from "this was never
    /// encrypted in the first place", so callers reading stores old enough to hold plain values have
    /// always had to treat a failure as the latter. A provider that authenticates its ciphertext can
    /// tell, and its failures must not be swallowed the same way — the tamper detection is the point
    /// of using it.
    /// </para>
    /// <para>
    /// Defaults to false: a provider that has not thought about the question has not got it.
    /// </para>
    /// </remarks>
    bool DetectsTampering => false;

    string Encrypt(string plainText, SecureString encryptionKey);

    string Decrypt(string cipherText, SecureString decryptionKey);
}