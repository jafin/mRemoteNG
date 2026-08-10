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

    string Encrypt(string plainText, SecureString encryptionKey);

    string Decrypt(string cipherText, SecureString decryptionKey);
}