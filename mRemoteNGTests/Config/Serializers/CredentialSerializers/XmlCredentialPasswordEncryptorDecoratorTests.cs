using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Xml.Linq;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.CredentialSerializer;
using mRemoteNG.Credential;
using mRemoteNG.Security;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Serializers.CredentialSerializers;

public class XmlCredentialPasswordEncryptorDecoratorTests
{
    private XmlCredentialPasswordEncryptorDecorator _sut;
    private const BlockCipherEngines CipherEngine = BlockCipherEngines.Twofish;
    private const BlockCipherModes CipherMode = BlockCipherModes.EAX;
    private const int KdfIterations = 2000;
    private SecureString _key = "myKey1".ConvertToSecureString();

    [SetUp]
    public void Setup()
    {
        var cryptoProvider = SetupCryptoProvider();
        var baseSerializer = SetupBaseSerializer();
        _sut = new XmlCredentialPasswordEncryptorDecorator(cryptoProvider, baseSerializer);
    }


    [Test]
    public void CantPassNullCredentialList()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Serialize(null, new SecureString()));
    }

    [Test]
    public void TheCredentialFileStaysClassic()
    {
        // A settled decision, not an omission. The credential file has no storage format level, so
        // there is nowhere to record a choice and no way for a user to make one — hardening it would
        // break upstream mRemoteNG unconditionally rather than on request.
        var cryptoProvider = SetupCryptoProvider();
        cryptoProvider.KeyDerivationPrf = System.Security.Cryptography.HashAlgorithmName.SHA256;

        // Constructing the decorator is what pins it, because the provider comes from outside and a
        // caller configured for the connection file would otherwise harden this by accident.
        _ = new XmlCredentialPasswordEncryptorDecorator(cryptoProvider, SetupBaseSerializer());

        Assert.That(cryptoProvider.KeyDerivationPrf,
            Is.EqualTo(mRemoteNG.Security.KeyDerivation.KeyDerivationPrf.Default));
    }

    [Test]
    public void TheCredentialFileRecordsNoPseudoRandomFunction()
    {
        string xml = _sut.Serialize([new CredentialRecord { Password = "pass".ConvertToSecureString() }], _key);

        Assert.That(xml, Does.Not.Contain(mRemoteNG.Security.KeyDerivation.KeyDerivationPrf.AttributeName));
    }

    [Test]
    public void EncryptsPasswordAttributesInXml()
    {
        var credList = Substitute.For<IEnumerable<ICredentialRecord>>();
        var output = _sut.Serialize(credList, _key);
        var outputAsXdoc = XDocument.Parse(output);
        var firstElementPassword = outputAsXdoc.Root?.Descendants().First().FirstAttribute.Value;
        Assert.That(firstElementPassword, Is.EqualTo("encrypted"));
    }

    [TestCase("EncryptionEngine", CipherEngine)]
    [TestCase("BlockCipherMode", CipherMode)]
    [TestCase("KdfIterations", KdfIterations)]
    [TestCase("Auth", "encrypted")]
    public void SetsRootNodeEncryptionAttributes(string attributeName, object expectedValue)
    {
        var credList = Substitute.For<IEnumerable<ICredentialRecord>>();
        var output = _sut.Serialize(credList, _key);
        var outputAsXdoc = XDocument.Parse(output);
        var authField = outputAsXdoc.Root?.Attribute(attributeName)?.Value;
        Assert.That(authField, Is.EqualTo(expectedValue.ToString()));
    }

    private static ISerializer<IEnumerable<ICredentialRecord>, string> SetupBaseSerializer()
    {
        var baseSerializer = Substitute.For<ISerializer<IEnumerable<ICredentialRecord>, string>>();
        var randomString = Guid.NewGuid().ToString();
        baseSerializer.Serialize(null).ReturnsForAnyArgs(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Root>" +
            $"<Element1 Password=\"{randomString}\" />" +
            $"<Element1 Password=\"{randomString}\">" +
            $"<Element1 Password=\"{randomString}\" />" +
            "</Element1>" +
            "</Root>");
        return baseSerializer;
    }

    private static ICryptographyProvider SetupCryptoProvider()
    {
        var cryptoProvider = Substitute.For<ICryptographyProvider>();
        cryptoProvider.CipherEngine.Returns(CipherEngine);
        cryptoProvider.CipherMode.Returns(CipherMode);
        cryptoProvider.KeyDerivationIterations.Returns(KdfIterations);
        cryptoProvider.Encrypt(null, null).ReturnsForAnyArgs("encrypted");
        return cryptoProvider;
    }
}