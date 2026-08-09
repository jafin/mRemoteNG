using System;
using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Security.Ssh;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

[TestFixture]
public class ResolvedSshCredentialTests
{
    [Test]
    public void SecretBufferIsZeroedOnDispose()
    {
        ResolvedSshCredential credential = new("alice", secret: "secret123");
        char[] buffer = credential.SecretBuffer!;

        Assert.That(new string(buffer), Is.EqualTo("secret123"), "precondition");

        credential.Dispose();

        Assert.That(buffer.All(c => c == '\0'), Is.True,
            "The buffer backing the secret must be zeroed on disposal.");
    }

    [Test]
    public void KeyMaterialBufferIsZeroedOnDispose()
    {
        ResolvedSshCredential credential = new("alice", keyMaterial: "-----BEGIN PRIVATE KEY-----");
        char[] buffer = credential.KeyMaterialBuffer!;

        credential.Dispose();

        Assert.That(buffer.All(c => c == '\0'), Is.True,
            "The buffer backing the key material must be zeroed on disposal.");
    }

    [Test]
    public void DisposeIsIdempotent()
    {
        ResolvedSshCredential credential = new("alice", secret: "secret123");

        credential.Dispose();

        Assert.DoesNotThrow(() => credential.Dispose());
    }

    [Test]
    public void RevealingASecretAfterDisposalThrows()
    {
        ResolvedSshCredential credential = new("alice", secret: "secret123");
        credential.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => credential.RevealSecret());
            Assert.Throws<ObjectDisposedException>(() => credential.RevealKeyMaterial());
        });
    }

    [Test]
    public void RevealSecretRoundTripsTheSecret()
    {
        using ResolvedSshCredential credential = new("alice", secret: "p@ss w0rd\\with\"quotes");

        Assert.That(credential.RevealSecret(), Is.EqualTo("p@ss w0rd\\with\"quotes"));
    }

    [Test]
    public void SecretSpanDoesNotMaterialiseAString()
    {
        using ResolvedSshCredential credential = new("alice", secret: "secret123");

        Assert.That(credential.SecretSpan.SequenceEqual("secret123".AsSpan()), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    public void AnAbsentSecretIsReportedAsAbsent(string? secret)
    {
        using ResolvedSshCredential credential = new("alice", secret: secret);

        Assert.Multiple(() =>
        {
            Assert.That(credential.HasSecret, Is.False);
            Assert.That(credential.RevealSecret(), Is.Empty);
            Assert.That(credential.SecretBuffer, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void AnEmptyPrivateKeyPathNormalisesToNull(string? path)
    {
        using ResolvedSshCredential credential = new("alice", privateKeyPath: path);

        Assert.That(credential.PrivateKeyPath, Is.Null);
    }

    [Test]
    public void AgentIdentitiesDefaultToEmptyRatherThanNull()
    {
        using ResolvedSshCredential credential = new("alice");

        Assert.Multiple(() =>
        {
            Assert.That(credential.AgentIdentities, Is.Not.Null);
            Assert.That(credential.AgentIdentities, Is.Empty);
            Assert.That(credential.HasAgentIdentities, Is.False);
        });
    }

    [Test]
    public void ProvenanceDefaultsToNoneMeaningInlineCredentials()
    {
        using ResolvedSshCredential credential = new("alice", secret: "secret123");

        Assert.That(credential.Provenance, Is.EqualTo(ExternalCredentialProvider.None));
    }

    [Test]
    public void ProvenanceIsCarriedThrough()
    {
        using ResolvedSshCredential credential = new(
            "alice", secret: "secret123", provenance: ExternalCredentialProvider.LAPS);

        Assert.That(credential.Provenance, Is.EqualTo(ExternalCredentialProvider.LAPS));
    }

    [Test]
    public void ANullUsernameIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new ResolvedSshCredential(null!));
    }

    // ---- logging contract ---------------------------------------------------

    [Test]
    public void ToStringNeverLeaksTheSecretOrKeyMaterial()
    {
        using ResolvedSshCredential credential = new(
            "alice",
            secret: "secret123",
            keyMaterial: "-----BEGIN OPENSSH PRIVATE KEY-----",
            provenance: ExternalCredentialProvider.DelineaSecretServer);

        string rendered = credential.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(rendered, Does.Not.Contain("secret123"));
            Assert.That(rendered, Does.Not.Contain("BEGIN OPENSSH PRIVATE KEY"));
            Assert.That(rendered, Does.Contain(nameof(ExternalCredentialProvider.DelineaSecretServer)),
                "Provenance is the one credential detail that may be logged.");
        });
    }

    // ---- agent identity model ------------------------------------------------

    [TestCase("sk-ssh-ed25519@openssh.com", true)]
    [TestCase("sk-ecdsa-sha2-nistp256@openssh.com", true)]
    [TestCase("ssh-ed25519", false)]
    [TestCase("ecdsa-sha2-nistp256", false)]
    [TestCase("ssh-rsa", false)]
    public void HardwareBackedIdentitiesAreRecognisedByAlgorithmPrefix(string algorithm, bool expected)
    {
        SshAgentIdentity identity = new("alice@laptop", algorithm);

        Assert.That(identity.IsHardwareBacked, Is.EqualTo(expected));
    }
}