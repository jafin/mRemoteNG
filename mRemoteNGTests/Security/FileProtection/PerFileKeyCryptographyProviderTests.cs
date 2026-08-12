using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Security.SymmetricEncryption;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// The provider that encrypts a connection file's contents under its own random key.
/// </summary>
/// <remarks>
/// Task 3.3: the unwrapped file key is used directly rather than fed to the KDF. It is 256 random
/// bits, so there is nothing to stretch — the KDF in this format applies to the recovery password
/// alone. The tests below fix that as behaviour rather than as an assertion about the source.
/// </remarks>
[TestFixture]
public class PerFileKeyCryptographyProviderTests
{
    private static readonly SecureString Ignored = "this is not the key".ConvertToSecureString();

    [Test]
    public void ContentRoundTripsUnderTheFileKey()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        string cipherText = provider.Encrypt("hunter2", Ignored);

        Assert.Multiple(() =>
        {
            Assert.That(cipherText, Is.Not.EqualTo("hunter2"));
            Assert.That(provider.Decrypt(cipherText, Ignored), Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void TheSuppliedPasswordIsIgnoredEntirely()
    {
        // Every call site in the serializers passes RootNodeInfo.PasswordString, because the
        // interface is shared with the password-keyed providers. At this protection level that string
        // is not what the file is keyed on, and a provider that quietly mixed it in would produce a
        // file that stops opening the moment the master password changes.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        string cipherText = provider.Encrypt("hunter2", "one password".ConvertToSecureString());

        Assert.That(provider.Decrypt(cipherText, "an entirely different password".ConvertToSecureString()),
            Is.EqualTo("hunter2"));
    }

    [Test]
    public void ADifferentFileKeyCannotRead()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        using ConnectionFileKey otherKey = ConnectionFileKey.Generate();

        string cipherText = new PerFileKeyCryptographyProvider(fileKey).Encrypt("hunter2", Ignored);

        Assert.Throws<EncryptionException>(
            () => new PerFileKeyCryptographyProvider(otherKey).Decrypt(cipherText, Ignored));
    }

    [Test]
    public void AlteredCiphertextIsRefusedRatherThanDecryptedToSomethingElse()
    {
        // The whole reason this format is authenticated. The legacy provider is AES-CBC with PKCS7
        // and no tag, so a wrong key there yields valid padding roughly once in 256 attempts and
        // returns arbitrary bytes — which the file then stores as a password.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        byte[] payload = System.Convert.FromBase64String(provider.Encrypt("hunter2", Ignored));
        payload[^1] ^= 0xFF;

        Assert.Throws<EncryptionException>(
            () => provider.Decrypt(System.Convert.ToBase64String(payload), Ignored));
    }

    [Test]
    public void APasswordKeyedPayloadIsNotMistakenForThisOne()
    {
        // The two formats have to be mutually unreadable. A caller that picked the wrong provider
        // must fail rather than produce plausible bytes, which is why this one carries its own
        // framing instead of reusing the AEAD provider's.
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();

        string fromAead = new AeadCryptographyProvider { KeyDerivationIterations = 1000 }
            .Encrypt("hunter2", "some password".ConvertToSecureString());

        Assert.Throws<EncryptionException>(
            () => new PerFileKeyCryptographyProvider(fileKey).Decrypt(fromAead, Ignored));
    }

    [Test]
    public void TheSamePlaintextEncryptsDifferentlyEachTime()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        Assert.That(provider.Encrypt("hunter2", Ignored), Is.Not.EqualTo(provider.Encrypt("hunter2", Ignored)));
    }

    [Test]
    public void EmptyValuesArePassedThroughAsTheOtherProvidersDo()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        Assert.Multiple(() =>
        {
            Assert.That(provider.Encrypt("", Ignored), Is.Empty);
            Assert.That(provider.Decrypt("", Ignored), Is.Empty);
        });
    }

    [Test]
    public void TheProviderOutlivesTheKeyItWasBuiltFrom()
    {
        // The load path disposes the file key as soon as it has built its providers, so the copy
        // taken in the constructor is what makes a later save work at all.
        ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);
        fileKey.Dispose();

        Assert.That(provider.Decrypt(provider.Encrypt("hunter2", Ignored), Ignored), Is.EqualTo("hunter2"));
    }

    [Test]
    public void TheProviderDescribesItselfAsWhatItActuallyUses()
    {
        using ConnectionFileKey fileKey = ConnectionFileKey.Generate();
        PerFileKeyCryptographyProvider provider = new(fileKey);

        Assert.Multiple(() =>
        {
            Assert.That(provider.CipherEngine, Is.EqualTo(BlockCipherEngines.AES));
            Assert.That(provider.CipherMode, Is.EqualTo(BlockCipherModes.GCM));
        });
    }
}
