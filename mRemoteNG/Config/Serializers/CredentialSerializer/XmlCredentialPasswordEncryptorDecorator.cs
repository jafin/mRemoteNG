using System;
using System.Collections.Generic;
using System.Security;
using System.Xml.Linq;
using mRemoteNG.Credential;
using mRemoteNG.Security;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Config.Serializers.CredentialSerializer;

public class XmlCredentialPasswordEncryptorDecorator : ISecureSerializer<IEnumerable<ICredentialRecord>, string>
{
    private readonly ISerializer<IEnumerable<ICredentialRecord>, string> _baseSerializer;
    private readonly ICryptographyProvider _cryptographyProvider;

    public XmlCredentialPasswordEncryptorDecorator(ICryptographyProvider cryptographyProvider, ISerializer<IEnumerable<ICredentialRecord>, string> baseSerializer)
    {
        ArgumentNullException.ThrowIfNull(baseSerializer);
        ArgumentNullException.ThrowIfNull(cryptographyProvider);

        _baseSerializer = baseSerializer;
        _cryptographyProvider = cryptographyProvider;

        // The credential file stays classic permanently — a settled decision, not an omission.
        //
        // It has no storage format level, so there is nowhere to record a choice and no way for a
        // user to make one: hardening it would break upstream mRemoteNG unconditionally rather than
        // on request, which is the whole thing add-storage-format-opt-in exists to prevent. Pinned
        // here rather than left to the caller, because the provider is supplied from outside and a
        // caller configured for the connection file would otherwise harden this by accident.
        _cryptographyProvider.KeyDerivationPrf = KeyDerivationPrf.Default;
    }


    public string Serialize(IEnumerable<ICredentialRecord> credentialRecords, SecureString key)
    {
        ArgumentNullException.ThrowIfNull(credentialRecords);

        string baseReturn = _baseSerializer.Serialize(credentialRecords);
        string encryptedReturn = EncryptPasswordAttributes(baseReturn, key);
        return encryptedReturn;
    }

    private string EncryptPasswordAttributes(string xml, SecureString encryptionKey)
    {
        XDocument xdoc = XDocument.Parse(xml);
        SetEncryptionAttributes(xdoc, encryptionKey);
        foreach (XElement element in xdoc.Descendants())
        {
            XAttribute? passwordAttribute = element.Attribute("Password");
            if (passwordAttribute == null) continue;
            string encryptedPassword = _cryptographyProvider.Encrypt(passwordAttribute.Value, encryptionKey);
            passwordAttribute.Value = encryptedPassword;
        }

        return xdoc.Declaration + Environment.NewLine + xdoc;
    }

    private void SetEncryptionAttributes(XDocument xdoc, SecureString encryptionKey)
    {
        xdoc.Root?.SetAttributeValue("EncryptionEngine", _cryptographyProvider.CipherEngine);
        xdoc.Root?.SetAttributeValue("BlockCipherMode", _cryptographyProvider.CipherMode);
        xdoc.Root?.SetAttributeValue("KdfIterations", _cryptographyProvider.KeyDerivationIterations);
        xdoc.Root?.SetAttributeValue("Auth", _cryptographyProvider.Encrypt(RandomGenerator.RandomString(20), encryptionKey));
    }
}