using System;
using System.IO;
using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

/// <summary>
/// Covers the <c>ssh-credential-diagnostics</c> requirements: a key the user chose failing is an
/// error, a key discovery went looking for failing is not, and a missing username is reported
/// rather than thrown.
/// </summary>
/// <remarks>
/// Written after a native terminal connection reported, at error severity, that a
/// passphrase-protected <c>~/.ssh/id_rsa</c> could not be loaded — during a connection that then
/// succeeded by other means. Nobody had configured that key; discovery found it.
/// </remarks>
[TestFixture]
public class SshCredentialDiagnosticSeverityTests
{
    private sealed class StubKeyLocator(string? key) : IDefaultSshKeyLocator
    {
        public string? Locate(DefaultKeyDiscoveryMode mode) => key;
    }

    private static ConnectionInfo Connection(string username = "alice", string keyPath = "") =>
        new() { Hostname = "example.invalid", Username = username, PrivateKeyPath = keyPath };

    private string _encryptedKey = string.Empty;

    [SetUp]
    public void WriteAnUnloadableKey()
    {
        // Not a valid key: loading must fail for a reason that is not "missing".
        _encryptedKey = Path.Combine(Path.GetTempPath(), $"mrng-key-{Guid.NewGuid():N}");
        File.WriteAllText(_encryptedKey,
            "-----BEGIN OPENSSH PRIVATE KEY-----\nnot-a-real-key\n-----END OPENSSH PRIVATE KEY-----\n");
    }

    [TearDown]
    public void RemoveKey()
    {
        if (File.Exists(_encryptedKey))
            File.Delete(_encryptedKey);
    }

    // ---- provenance -----------------------------------------------------------

    [Test]
    public void AKeyNamedOnTheConnectionIsConfigured()
    {
        using ResolvedSshCredential credential = SshCredentialResolver
            .CreateDefault(new StubKeyLocator("C:\\discovered\\id_rsa"), null)
            .Resolve(Connection(keyPath: _encryptedKey), SshCredentialResolutionOptions.ForSshNet(false));

        Assert.Multiple(() =>
        {
            Assert.That(credential.KeyPathOrigin, Is.EqualTo(SshKeyPathOrigin.Configured));
            Assert.That(credential.PrivateKeyPath, Is.EqualTo(_encryptedKey),
                "a configured key must win over discovery");
        });
    }

    [Test]
    public void AKeyFoundByDiscoveryIsDiscovered()
    {
        using ResolvedSshCredential credential = SshCredentialResolver
            .CreateDefault(new StubKeyLocator(_encryptedKey), null)
            .Resolve(Connection(), SshCredentialResolutionOptions.ForSshNet(false));

        Assert.That(credential.KeyPathOrigin, Is.EqualTo(SshKeyPathOrigin.Discovered));
    }

    [Test]
    public void WithNoKeyAtAllThereIsNoOrigin()
    {
        using ResolvedSshCredential credential = SshCredentialResolver
            .CreateDefault(new StubKeyLocator(null), null)
            .Resolve(Connection(), SshCredentialResolutionOptions.ForSshNet(false));

        Assert.Multiple(() =>
        {
            Assert.That(credential.PrivateKeyPath, Is.Null);
            Assert.That(credential.KeyPathOrigin, Is.EqualTo(SshKeyPathOrigin.None));
        });
    }

    // ---- severity -------------------------------------------------------------

    [Test]
    public void AnUnusableDiscoveredKeyIsInformationAndIsNotCalledConfigured()
    {
        using ResolvedSshCredential credential = new("alice",
            privateKeyPath: _encryptedKey, keyPathOrigin: SshKeyPathOrigin.Discovered);

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.Multiple(() =>
        {
            Assert.That(authentication.Unsupported, Is.Not.Empty);
            Assert.That(authentication.Unsupported.Select(u => u.Severity),
                Is.All.EqualTo(SshCredentialDiagnosticSeverity.Information),
                "a key nobody configured failing is not an error the user must act on");
            Assert.That(authentication.Unsupported[0].Message, Does.Not.Contain("configured private key"));
        });
    }

    [Test]
    public void AnUnusableConfiguredKeyIsStillAnError()
    {
        // The regression this change is most likely to cause, so it is pinned.
        using ResolvedSshCredential credential = new("alice",
            privateKeyPath: _encryptedKey, keyPathOrigin: SshKeyPathOrigin.Configured);

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.That(authentication.Unsupported.Select(u => u.Severity),
            Does.Contain(SshCredentialDiagnosticSeverity.Error));
    }

    [Test]
    public void AMissingConfiguredKeyIsStillAnError()
    {
        using ResolvedSshCredential credential = new("alice",
            privateKeyPath: Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"),
            keyPathOrigin: SshKeyPathOrigin.Configured);

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.Multiple(() =>
        {
            Assert.That(authentication.Unsupported.Select(u => u.Severity),
                Does.Contain(SshCredentialDiagnosticSeverity.Error));
            Assert.That(authentication.Unsupported[0].Message, Does.Contain("was not found"));
        });
    }

    [Test]
    public void ADefaultConstructedCredentialTreatsItsKeyAsConfigured()
    {
        // Every existing construction keeps today's meaning; only discovery says otherwise.
        using ResolvedSshCredential credential = new("alice", privateKeyPath: _encryptedKey);

        Assert.That(credential.KeyPathOrigin, Is.EqualTo(SshKeyPathOrigin.Configured));
    }

    // ---- what was actually offered --------------------------------------------

    [Test]
    public void AKeyThatFailedToLoadIsNotReportedAsOffered()
    {
        // It contributed nothing, so the server never saw it. Naming it points the reader at a
        // file that had no part in the refusal — the class of half-true message that made an
        // earlier failure take four rounds to diagnose.
        using ResolvedSshCredential credential = new("alice",
            privateKeyPath: _encryptedKey, keyPathOrigin: SshKeyPathOrigin.Discovered);

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.That(authentication.KeyFileOffered, Is.Null);
    }

    [Test]
    public void AMissingKeyIsNotReportedAsOffered()
    {
        using ResolvedSshCredential credential = new("alice",
            privateKeyPath: Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"),
            keyPathOrigin: SshKeyPathOrigin.Configured);

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.That(authentication.KeyFileOffered, Is.Null);
    }

    [Test]
    public void WithNoKeyConfiguredNothingIsReportedAsOffered()
    {
        using ResolvedSshCredential credential = new("alice", secret: "hunter2");

        using SshNetAuthentication authentication = SshNetAuthAdapter.Translate(credential);

        Assert.That(authentication.KeyFileOffered, Is.Null);
    }

    // ---- missing username -----------------------------------------------------

    [Test]
    public void AMissingUsernameIsReportedRatherThanThrown()
    {
        // Previously threw ArgumentException out of SSH.NET's AuthenticationMethod constructor.
        // Callers treat translation as total, so it surfaced as an unhandled failure inside a
        // credential adapter rather than as the setting it describes.
        using ResolvedSshCredential credential = new("   ", privateKeyPath: _encryptedKey);

        SshNetAuthentication authentication = null!;
        Assert.DoesNotThrow(() => authentication = SshNetAuthAdapter.Translate(credential));

        using (authentication)
        {
            Assert.Multiple(() =>
            {
                Assert.That(authentication.Methods, Is.Empty);
                Assert.That(authentication.Unsupported.Select(u => u.Message),
                    Has.Some.Contains("no username"));
                Assert.That(authentication.Unsupported.Select(u => u.Severity),
                    Does.Contain(SshCredentialDiagnosticSeverity.Error));
            });
        }
    }
}
