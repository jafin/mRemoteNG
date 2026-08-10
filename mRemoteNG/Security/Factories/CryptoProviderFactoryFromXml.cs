using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;
using mRemoteNG.Security.AsymmetricEncryption;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Security.Factories;

[SupportedOSPlatform("windows")]
public class CryptoProviderFactoryFromXml : ICryptoProviderFactory
{
    private readonly XElement _element;

    public CryptoProviderFactoryFromXml(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _element = element;
    }

    public ICryptographyProvider Build()
    {
        // Certificate-based encryption takes precedence: if a thumbprint is stored
        // in the file, route to CertificateCryptographyProvider regardless of the
        // EncryptionEngine / BlockCipherMode attributes (which reflect the internal
        // AES-GCM layer, not the outer RSA key-wrapping layer).
        string? thumbprint = _element?.Attribute("CertificateThumbprint")?.Value;
        if (!string.IsNullOrWhiteSpace(thumbprint))
            return new CertificateCryptographyProvider(thumbprint);

        ICryptographyProvider cryptoProvider;
        try
        {
            BlockCipherEngines engine = Enum.Parse<BlockCipherEngines>(
                _element?.Attribute("EncryptionEngine")?.Value ?? "");
            BlockCipherModes mode = Enum.Parse<BlockCipherModes>(
                _element?.Attribute("BlockCipherMode")?.Value ?? "");
            cryptoProvider = new CryptoProviderFactory(engine, mode).Build();

            int keyDerivationIterations = int.Parse(_element?.Attribute("KdfIterations")?.Value ?? "", CultureInfo.InvariantCulture);
            cryptoProvider.KeyDerivationIterations = Math.Clamp(keyDerivationIterations, 1000, 10_000_000);

            // Absent or unrecognised means SHA-1, which is what every file written before this was
            // recorded used. Derived with what the file states, never with what is configured now.
            cryptoProvider.KeyDerivationPrf =
                KeyDerivationPrf.Parse(_element?.Attribute(KeyDerivationPrf.AttributeName)?.Value);
        }
        catch (Exception)
        {
            return new LegacyRijndaelCryptographyProvider();
        }

        return cryptoProvider;
    }
}