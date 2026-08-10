using System;
using System.Runtime.Versioning;
using System.Xml.Linq;
using mRemoteNG.Security;
using mRemoteNG.Security.AsymmetricEncryption;
using mRemoteNG.Tree.Root;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;

[SupportedOSPlatform("windows")]
public static class XmlRootNodeSerializer
{
    /// <param name="storageFormatOverride">
    /// The level to record instead of the root node's own. Supplied by callers writing a copy rather
    /// than the store itself — an export has to stay readable by upstream mRemoteNG whatever the
    /// store it came from is, and inheriting the level would make a hardened store produce exports
    /// nothing else can open.
    /// </param>
    public static XElement SerializeRootNodeInfo(RootNodeInfo rootNodeInfo, ICryptographyProvider cryptographyProvider, Version version, bool fullFileEncryption = false, StorageFormatLevel? storageFormatOverride = null)
    {
        XNamespace xmlNamespace = "http://mremoteng.org";
        XElement element = new(xmlNamespace + "Connections");
        element.Add(new XAttribute(XNamespace.Xmlns + "mrng", xmlNamespace));
        element.Add(new XAttribute(XName.Get("Name"), rootNodeInfo.Name));
        element.Add(new XAttribute(XName.Get("Export"), "false"));
        element.Add(new XAttribute(XName.Get("EncryptionEngine"), cryptographyProvider.CipherEngine));
        element.Add(new XAttribute(XName.Get("BlockCipherMode"), cryptographyProvider.CipherMode));
        element.Add(new XAttribute(XName.Get("KdfIterations"), cryptographyProvider.KeyDerivationIterations));

        // Beside the iteration count, for the same reason it is recorded: a file outlives the build
        // that wrote it. Written only when it is not SHA-1, so a classic file stays byte-compatible
        // with what upstream mRemoteNG writes and reads.
        string? kdfPrf = KeyDerivationPrf.ToRecordedValue(cryptographyProvider.KeyDerivationPrf);
        if (kdfPrf is not null)
            element.Add(new XAttribute(XName.Get(KeyDerivationPrf.AttributeName), kdfPrf));
        if (cryptographyProvider is CertificateCryptographyProvider certProvider)
            element.Add(new XAttribute(XName.Get("CertificateThumbprint"), certProvider.Thumbprint));
        element.Add(new XAttribute(XName.Get("FullFileEncryption"), fullFileEncryption.ToString().ToLowerInvariant()));
        element.Add(new XAttribute(XName.Get("AutoLockOnMinimize"), rootNodeInfo.AutoLockOnMinimize.ToString().ToLowerInvariant()));
        if (rootNodeInfo.TotpEnabled && !string.IsNullOrEmpty(rootNodeInfo.TotpSecret))
        {
            element.Add(new XAttribute(XName.Get("TotpEnabled"), "true"));
            using System.Security.SecureString encryptionPassword = rootNodeInfo.PasswordString.ConvertToSecureString();
            element.Add(new XAttribute(XName.Get("TotpSecret"), cryptographyProvider.Encrypt(rootNodeInfo.TotpSecret, encryptionPassword)));
        }
        element.Add(CreateProtectedAttribute(rootNodeInfo, cryptographyProvider));
        element.Add(new XAttribute(XName.Get("ConfVersion"), version.ToString(2)));

        // Written only when hardened. A classic file has to come out byte-compatible with what
        // upstream mRemoteNG writes, because it reads this same file from this same path — so
        // absence is what means classic, and adding an attribute here unconditionally would be the
        // silent format change the level exists to prevent.
        string? storageFormat = StorageFormat.ToRecordedValue(storageFormatOverride ?? rootNodeInfo.StorageFormat);
        if (storageFormat is not null)
            element.Add(new XAttribute(XName.Get(StorageFormat.AttributeName), storageFormat));

        return element;
    }

    private static XAttribute CreateProtectedAttribute(RootNodeInfo rootNodeInfo, ICryptographyProvider cryptographyProvider)
    {
        XAttribute attribute = new(XName.Get("Protected"), "");
        string plainText = (rootNodeInfo.PasswordString != rootNodeInfo.DefaultPassword) ? ConnectionFileDefaults.ProtectedSentinel : ConnectionFileDefaults.NotProtectedSentinel;
        using System.Security.SecureString encryptionPassword = rootNodeInfo.PasswordString.ConvertToSecureString();
        attribute.Value = cryptographyProvider.Encrypt(plainText, encryptionPassword);
        return attribute;
    }
}