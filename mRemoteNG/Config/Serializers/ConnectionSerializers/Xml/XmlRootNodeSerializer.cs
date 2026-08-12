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

        StorageFormatLevel effectiveLevel = storageFormatOverride ?? rootNodeInfo.StorageFormat;

        // Beside the iteration count, for the same reason it is recorded: a file outlives the build
        // that wrote it. Written only when it is not SHA-1, so a classic file stays byte-compatible
        // with what upstream mRemoteNG writes and reads.
        string? kdfPrf = KeyDerivationPrf.ToRecordedValue(cryptographyProvider.KeyDerivationPrf);

        // A classic file carrying a hardened function is the one combination that breaks the
        // guarantee: upstream mRemoteNG ignores the attribute it does not know, derives with SHA-1,
        // and reports the failure as a wrong password on a file the user has the password to. The
        // level and the function come from different places — the level from the store or an
        // override, the function from the provider — so nothing but this stops them diverging.
        //
        // The opposite pairing is left alone deliberately. A hardened marker with SHA-1 derivation
        // still opens everywhere, because upstream ignores the marker too and absence of the
        // function already means SHA-1.
        if (kdfPrf is not null && effectiveLevel != StorageFormatLevel.Hardened)
            throw new InvalidOperationException(
                $"Refusing to write a {StorageFormat.Describe(effectiveLevel)} store with a hardened key derivation function " +
                $"({kdfPrf}). Upstream mRemoteNG would not be able to open it.");

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
        // A per-file key applies only at the hardened level, and only to the store itself. An export
        // overrides the level to classic precisely so it stays readable by upstream mRemoteNG, and
        // that has to take the protectors off with it: a copy carrying a machine-bound protector is
        // a copy that opens on one machine, which is the opposite of what an escape route is for.
        // Reading the effective level rather than the root's own is what makes that automatic.
        bool usePerFileKey = effectiveLevel == StorageFormatLevel.Hardened && rootNodeInfo.KeyProtection is not null;

        element.Add(CreateProtectedAttribute(rootNodeInfo, cryptographyProvider, usePerFileKey));
        element.Add(new XAttribute(XName.Get("ConfVersion"), version.ToString(2)));

        if (usePerFileKey)
            rootNodeInfo.KeyProtection!.WriteTo(element);

        // Written only when hardened. A classic file has to come out byte-compatible with what
        // upstream mRemoteNG writes, because it reads this same file from this same path — so
        // absence is what means classic, and adding an attribute here unconditionally would be the
        // silent format change the level exists to prevent.
        string? storageFormat = StorageFormat.ToRecordedValue(effectiveLevel);
        if (storageFormat is not null)
            element.Add(new XAttribute(XName.Get(StorageFormat.AttributeName), storageFormat));

        return element;
    }

    /// <param name="usePerFileKey">
    /// Whether the store is keyed on itself. The sentinel is the only ciphertext in the file whose
    /// plaintext is known in advance, so it is what the reader checks an unwrapped key against — which
    /// makes writing the right one of the three values load-bearing rather than descriptive.
    /// </param>
    private static XAttribute CreateProtectedAttribute(RootNodeInfo rootNodeInfo, ICryptographyProvider cryptographyProvider, bool usePerFileKey)
    {
        XAttribute attribute = new(XName.Get("Protected"), "");

        string plainText = usePerFileKey
            ? ConnectionFileDefaults.PerFileKeySentinel
            : (rootNodeInfo.PasswordString != rootNodeInfo.DefaultPassword) ? ConnectionFileDefaults.ProtectedSentinel : ConnectionFileDefaults.NotProtectedSentinel;

        // Ignored by the per-file provider, which is keyed on the file rather than on this string.
        using System.Security.SecureString encryptionPassword = rootNodeInfo.PasswordString.ConvertToSecureString();
        attribute.Value = cryptographyProvider.Encrypt(plainText, encryptionPassword);
        return attribute;
    }
}