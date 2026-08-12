using System.Security;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

/// <summary>
/// The root sentinel says how a store is protected, and a value this build does not know is refused
/// rather than read as the legacy default.
/// </summary>
/// <remarks>
/// The sentinel is the only ciphertext in the file whose plaintext is known in advance, which makes
/// it the only one that can tell a right key from a wrong one. That matters because the legacy
/// provider is AES-CBC with PKCS7 and no authentication tag: a wrong key produces valid padding
/// often enough to matter and returns arbitrary bytes rather than failing.
/// </remarks>
[TestFixture]
public class ConnectionFileSentinelTests
{
    [Test]
    public void TheThreeFormatSentinelsAreRecognisedAndNothingElseIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConnectionFileDefaults.IsKnownSentinel(ConnectionFileDefaults.ProtectedSentinel));
            Assert.That(ConnectionFileDefaults.IsKnownSentinel(ConnectionFileDefaults.NotProtectedSentinel));
            Assert.That(ConnectionFileDefaults.IsKnownSentinel(ConnectionFileDefaults.PerFileKeySentinel));

            Assert.That(ConnectionFileDefaults.IsKnownSentinel(null), Is.False);
            Assert.That(ConnectionFileDefaults.IsKnownSentinel(""), Is.False);
            Assert.That(ConnectionFileDefaults.IsKnownSentinel("ThisIsSomethingElse"), Is.False);
            // Case matters: the comparison is ordinal because the value is written by this code,
            // not typed by anyone.
            Assert.That(ConnectionFileDefaults.IsKnownSentinel("thisisprotected"), Is.False);
        });
    }

    [Test]
    public void TheThreeSentinelsAreDistinct()
    {
        Assert.That(new[]
        {
            ConnectionFileDefaults.ProtectedSentinel,
            ConnectionFileDefaults.NotProtectedSentinel,
            ConnectionFileDefaults.PerFileKeySentinel
        }, Is.Unique);
    }

    [Test]
    public void AnUnprotectedStoreIsStillRecognisedWithoutAPrompt()
    {
        // The existing two values must behave exactly as they did. This is the default-key case,
        // which has to keep opening with no password requested.
        RootNodeInfo root = new(RootNodeType.Connection);
        XmlConnectionsDecryptor decryptor = new(root) { AuthenticationRequestor = NeverCalled };

        string sentinel = new LegacyRijndaelCryptographyProvider().Encrypt(
            ConnectionFileDefaults.NotProtectedSentinel,
            ConnectionFileDefaults.LegacyEncryptionKey.ConvertToSecureString());

        Assert.That(decryptor.ConnectionsFileIsAuthentic(sentinel, root.PasswordString.ConvertToSecureString()));
    }

    [Test]
    public void ASentinelThisBuildDoesNotKnowIsNotTreatedAsUnprotected()
    {
        // The refusal this change exists for. The sentinel below decrypts cleanly under the legacy
        // default key — the key is right and decryption succeeds — but the plaintext is a value this
        // build does not know. Without the validator that counted as authentication and the store
        // opened as though it were the unprotected legacy case.
        //
        // A requestor is supplied deliberately: without one the authenticator returns false before
        // it ever decrypts, and the assertion would hold whether or not the validator existed.
        RootNodeInfo root = new(RootNodeType.Connection);
        int prompts = 0;

        XmlConnectionsDecryptor decryptor = new(root)
        {
            AuthenticationRequestor = () =>
            {
                prompts++;
                return ConnectionFileDefaults.LegacyEncryptionKey.ConvertToSecureString();
            }
        };

        string sentinel = new LegacyRijndaelCryptographyProvider().Encrypt(
            "ThisIsFromANewerBuild",
            ConnectionFileDefaults.LegacyEncryptionKey.ConvertToSecureString());

        Assert.Multiple(() =>
        {
            Assert.That(decryptor.ConnectionsFileIsAuthentic(sentinel, root.PasswordString.ConvertToSecureString()),
                Is.False, "an unrecognised sentinel is not accepted as proof of the key");
            Assert.That(prompts, Is.GreaterThan(0),
                "and the value was rejected rather than accepted on its first decryption");
        });
    }

    private static Optional<SecureString> NeverCalled()
    {
        Assert.Fail("no password should have been requested for an unprotected store");
        return new SecureString();
    }
}
