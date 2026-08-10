using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.Authentication;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

public class ConnectionFileDefaultsTests
{
    [TestCase(ConnectionFileDefaults.ProtectedSentinel)]
    [TestCase(ConnectionFileDefaults.NotProtectedSentinel)]
    public void KnownSentinelsAreRecognised(string sentinel) =>
        Assert.That(ConnectionFileDefaults.IsKnownSentinel(sentinel));

    [TestCase("")]
    [TestCase(null)]
    [TestCase("thisisprotected")]
    [TestCase("ThisIsProtected ")]
    [TestCase("")]
    public void AnythingElseIsNot(string? value) =>
        Assert.That(ConnectionFileDefaults.IsKnownSentinel(value), Is.False);

    /// <summary>
    /// The defect this check exists for: a decryption that completed is not evidence of the key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy provider is AES-CBC with PKCS7 and no authentication tag. A wrong key yields
    /// valid padding roughly once in 256 attempts, and then returns arbitrary bytes instead of
    /// throwing — so a caller that treats "did not throw" as success accepts a wrong password at
    /// about that rate.
    /// </para>
    /// <para>
    /// This does not brute-force an actual colliding key: that would be a slow, probabilistic test
    /// of the .NET padding implementation rather than of our own code. It reproduces the observable
    /// condition directly — decryption succeeds and the plaintext is not a sentinel — which is
    /// exactly what the caller has to decide on.
    /// </para>
    /// </remarks>
    [Test]
    public void ADecryptionThatSucceedsButYieldsANonSentinelIsRejected()
    {
        LegacyRijndaelCryptographyProvider provider = new();
        SecureString key = "someKey".ConvertToSecureString();
        string cipherText = provider.Encrypt("not a sentinel", key);

        string decrypted = provider.Decrypt(cipherText, key);

        Assert.Multiple(() =>
        {
            Assert.That(decrypted, Is.EqualTo("not a sentinel"), "decryption completed without error");
            Assert.That(ConnectionFileDefaults.IsKnownSentinel(decrypted), Is.False,
                "and is still rejected, because the plaintext is what proves the key");
        });
    }

    [Test]
    public void AuthenticatorWiredWithTheSentinelCheckRejectsANonSentinel()
    {
        LegacyRijndaelCryptographyProvider provider = new();
        SecureString key = "someKey".ConvertToSecureString();

        PasswordAuthenticator authenticator =
            new(provider, provider.Encrypt("not a sentinel", key), () => Optional<SecureString>.Empty)
            {
                PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel
            };

        // Before the check existed this returned true: the key is correct, so Decrypt did not throw.
        Assert.That(authenticator.Authenticate(key), Is.False);
    }

    [Test]
    public void AuthenticatorWiredWithTheSentinelCheckAcceptsASentinel()
    {
        LegacyRijndaelCryptographyProvider provider = new();
        SecureString key = "someKey".ConvertToSecureString();

        PasswordAuthenticator authenticator =
            new(provider, provider.Encrypt(ConnectionFileDefaults.NotProtectedSentinel, key), () => Optional<SecureString>.Empty)
            {
                PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel
            };

        Assert.That(authenticator.Authenticate(key));
        Assert.That(authenticator.LastAuthenticatedPassword, Is.SameAs(key));
    }
}
