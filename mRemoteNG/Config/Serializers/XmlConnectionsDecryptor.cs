using System;
using System.Runtime.Versioning;
using System.Security;
using System.Threading.Tasks;
using mRemoteNG.Security;
using mRemoteNG.Security.Authentication;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using System.Security.Cryptography;

namespace mRemoteNG.Config.Serializers;

[SupportedOSPlatform("windows")]
public class XmlConnectionsDecryptor
{
    private readonly ICryptographyProvider _cryptographyProvider;
    private readonly RootNodeInfo _rootNodeInfo;
    private readonly BlockCipherEngines? _cipherEngine;
    private readonly BlockCipherModes? _cipherMode;
    private readonly bool _providerIsShareable;
    private SecureString? _cachedDecryptionKey;

    public Func<Optional<SecureString>>? AuthenticationRequestor { get; set; }

    public int KeyDerivationIterations
    {
        get => _cryptographyProvider.KeyDerivationIterations;
        set => _cryptographyProvider.KeyDerivationIterations = value;
    }

    /// <summary>
    /// The PBKDF2 function the file states it was written with.
    /// </summary>
    /// <remarks>
    /// Carried alongside the iteration count for the same reason: both have to come from the file
    /// rather than from what this build happens to be configured with, or a file written under one
    /// set of parameters cannot be opened under another.
    /// </remarks>
    public HashAlgorithmName KeyDerivationPrf
    {
        get => _cryptographyProvider.KeyDerivationPrf;
        set => _cryptographyProvider.KeyDerivationPrf = value;
    }


    public XmlConnectionsDecryptor(RootNodeInfo rootNodeInfo)
    {
        _cryptographyProvider = new LegacyRijndaelCryptographyProvider();
        _rootNodeInfo = rootNodeInfo;
    }

    /// <summary>
    /// Decrypts with a provider the caller has already built.
    /// </summary>
    /// <remarks>
    /// For a store keyed on its own random key, where the engine, mode and iteration count recorded
    /// on the root describe nothing this provider does — there is no derivation to configure. The
    /// caller is the only thing holding the unwrapped key, so it is the only thing that can build the
    /// provider.
    /// </remarks>
    public XmlConnectionsDecryptor(ICryptographyProvider cryptographyProvider, RootNodeInfo rootNodeInfo)
    {
        ArgumentNullException.ThrowIfNull(cryptographyProvider);
        _cryptographyProvider = cryptographyProvider;
        _rootNodeInfo = rootNodeInfo;
        _providerIsShareable = true;
    }

    public XmlConnectionsDecryptor(BlockCipherEngines blockCipherEngine, BlockCipherModes blockCipherMode,
        RootNodeInfo rootNodeInfo)
    {
        _cipherEngine = blockCipherEngine;
        _cipherMode = blockCipherMode;
        _cryptographyProvider = new CryptoProviderFactory(blockCipherEngine, blockCipherMode).Build();
        _rootNodeInfo = rootNodeInfo;
    }

    private SecureString GetDecryptionKey()
    {
        return _cachedDecryptionKey ??= _rootNodeInfo.PasswordString.ConvertToSecureString();
    }

    private void InvalidateKeyCache()
    {
        _cachedDecryptionKey = null;
    }

    public string Decrypt(string plainText)
    {
        return plainText == ""
            ? ""
            : _cryptographyProvider.Decrypt(plainText, GetDecryptionKey());
    }

    /// <summary>
    /// Decrypts multiple ciphertexts in parallel using thread-local crypto providers.
    /// PBKDF2 key derivation dominates decrypt time (~100ms per call at 600K iterations),
    /// so parallelizing across CPU cores provides near-linear speedup.
    /// </summary>
    public string[] DecryptBatch(string[] cipherTexts)
    {
        string[] results = new string[cipherTexts.Length];
        if (cipherTexts.Length == 0) return results;

        SecureString key = GetDecryptionKey();

        Parallel.For(0, cipherTexts.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            CreateThreadLocalProvider,
            (i, _, localProvider) =>
            {
                results[i] = string.IsNullOrEmpty(cipherTexts[i])
                    ? ""
                    : localProvider.Decrypt(cipherTexts[i], key);
                return localProvider;
            },
            _ => { });

        return results;
    }

    private ICryptographyProvider CreateThreadLocalProvider()
    {
        // A supplied provider derives no key, so it holds no per-call state to race on and there is
        // nothing for a copy to be given: it is shared across the batch rather than duplicated. It
        // also could not be rebuilt here even if that were wanted, because the key it holds is not
        // recorded anywhere this class can reach.
        if (_providerIsShareable)
            return _cryptographyProvider;

        if (_cipherEngine == null)
            return new LegacyRijndaelCryptographyProvider();

        ICryptographyProvider provider = new CryptoProviderFactory(_cipherEngine.Value, _cipherMode!.Value).Build();
        provider.KeyDerivationIterations = KeyDerivationIterations;

        // The per-thread copies derive their own keys, so they need every parameter the file
        // recorded. Omitting this would make batch decryption silently fall back to SHA-1.
        provider.KeyDerivationPrf = KeyDerivationPrf;
        return provider;
    }

    public string LegacyFullFileDecrypt(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        if (xml.Contains("<?xml version=\"1.0\" encoding=\"utf-8\"?>", StringComparison.OrdinalIgnoreCase)) return xml;

        string decryptedContent = "";
        bool notDecr;

        try
        {
            decryptedContent = _cryptographyProvider.Decrypt(xml, GetDecryptionKey());
            notDecr = decryptedContent == xml;
        }
        catch (Exception)
        {
            notDecr = true;
        }

        if (notDecr)
        {
            if (Authenticate(xml, GetDecryptionKey()))
            {
                decryptedContent =
                    _cryptographyProvider.Decrypt(xml, GetDecryptionKey());
                notDecr = false;
            }

            if (notDecr == false)
                return decryptedContent;
        }
        else
        {
            return decryptedContent;
        }

        return "";
    }

    public bool ConnectionsFileIsAuthentic(string protectedString, SecureString password)
    {
        bool connectionsFileIsNotEncrypted = false;
        try
        {
            connectionsFileIsNotEncrypted =
                string.Equals(_cryptographyProvider.Decrypt(protectedString, GetDecryptionKey()), ConnectionFileDefaults.NotProtectedSentinel,
                    StringComparison.Ordinal);
        }
        catch (EncryptionException)
        {
            _ = 0; // Intentionally empty — file is not encrypted
        }

        // The sentinel is the one ciphertext whose plaintext is known in advance, so it is the one
        // that can be validated. Without this, any password whose decryption merely completed was
        // accepted: the legacy provider is AES-CBC with PKCS7 and no authentication tag, so a wrong
        // key yields valid padding about once in 256 and returns arbitrary bytes rather than
        // failing — and those arbitrary bytes were taken as proof of the password.
        //
        // It is also what stops an unrecognised sentinel falling through to the legacy key: a value
        // this build does not know is not ThisIsNotProtected, and guessing that it is would open a
        // store under the wrong assumption about how it is protected.
        return connectionsFileIsNotEncrypted ||
               Authenticate(protectedString, GetDecryptionKey(), ConnectionFileDefaults.IsKnownSentinel);
    }

    /// <param name="plaintextValidator">
    /// Optional check on what the decryption produced. Supplied only by the sentinel path, which is
    /// the one caller that knows what it encrypted.
    /// <para>
    /// It must not be applied to <see cref="LegacyFullFileDecrypt"/>, whose ciphertext is the whole
    /// connection file: the plaintext there is an XML document, never a sentinel, so validating it
    /// against the sentinel set would refuse every fully-encrypted legacy file that has a custom
    /// password — which is exactly what it did before this parameter existed.
    /// </para>
    /// </param>
    private bool Authenticate(string cipherText, SecureString password,
                              Func<string, bool>? plaintextValidator = null)
    {
        if (AuthenticationRequestor is null)
            return false;

        PasswordAuthenticator authenticator = new(_cryptographyProvider, cipherText, AuthenticationRequestor)
        {
            PlaintextValidator = plaintextValidator
        };

        bool authenticated = authenticator.Authenticate(password);

        if (!authenticated || authenticator.LastAuthenticatedPassword is null)
            return false;

        _rootNodeInfo.PasswordString = authenticator.LastAuthenticatedPassword.ConvertToUnsecureString();
        InvalidateKeyCache();
        return true;
    }
}