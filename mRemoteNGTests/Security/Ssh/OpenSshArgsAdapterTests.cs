using System;
using mRemoteNG.Connection;
using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Adapters;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

/// <summary>
/// Covers the "adapters report credentials they cannot honour" requirement in
/// <c>specs/ssh-credential-resolution/spec.md</c>.
/// </summary>
[TestFixture]
public class OpenSshArgsAdapterTests
{
    private const string Host = "example-host";

    // ---- unsupported password ------------------------------------------------

    [Test]
    public void AResolvedPasswordIsReportedAsUnsupported()
    {
        using ResolvedSshCredential credential = new("alice", secret: "secret123");

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unsupported, Has.Count.EqualTo(1));
            Assert.That(result.Unsupported[0].Message, Does.Contain("non-interactively"));
            Assert.That(result.Unsupported[0].Severity,
                Is.EqualTo(SshCredentialDiagnosticSeverity.Error));
        });
    }

    [Test]
    public void TheUnsupportedMessageNamesTheCredentialProvider()
    {
        using ResolvedSshCredential credential = new(
            "alice", secret: "secret123", provenance: ExternalCredentialProvider.DelineaSecretServer);

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.That(result.Unsupported[0].Message,
            Does.Contain(nameof(ExternalCredentialProvider.DelineaSecretServer)));
    }

    [Test]
    public void TheUnsupportedMessageNeverLeaksTheSecret()
    {
        using ResolvedSshCredential credential = new(
            "alice", secret: "secret123", provenance: ExternalCredentialProvider.LAPS);

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.That(result.Unsupported[0].Message, Does.Not.Contain("secret123"));
    }

    [Test]
    public void InlineCredentialsAreDescribedWithoutAProviderName()
    {
        using ResolvedSshCredential credential = new("alice", secret: "secret123");

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.That(result.Unsupported[0].Message, Does.Contain("this connection"));
    }

    [Test]
    public void ProviderSuppliedKeyMaterialIsAlsoReportedAsUnsupported()
    {
        using ResolvedSshCredential credential = new(
            "alice", keyMaterial: "PRIVATE-KEY-BODY",
            provenance: ExternalCredentialProvider.ClickstudiosPasswordState);

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unsupported, Has.Count.EqualTo(1));
            Assert.That(result.Unsupported[0].Message, Does.Contain("only as a file path"));
            Assert.That(result.Unsupported[0].Message, Does.Not.Contain("PRIVATE-KEY-BODY"));
        });
    }

    // ---- the no-warning case --------------------------------------------------

    [Test]
    public void AFullySupportedCredentialProducesNoWarning()
    {
        using ResolvedSshCredential credential = new(
            "alice", privateKeyPath: @"C:\keys\id_ed25519");

        OpenSshCredentialArguments result = OpenSshArgsAdapter.Translate(credential, Host);

        Assert.That(result.Unsupported, Is.Empty);
    }

    [Test]
    public void AKeyOnlyCredentialProducesNoWarning()
    {
        using ResolvedSshCredential credential = new("alice");

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).Unsupported, Is.Empty);
    }

    // ---- argument shape --------------------------------------------------------

    [Test]
    public void TheIdentityArgumentQuotesTheKeyPath()
    {
        using ResolvedSshCredential credential = new(
            "alice", privateKeyPath: @"C:\my keys\id_ed25519");

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).IdentityArgument,
            Is.EqualTo(@"-i ""C:\my keys\id_ed25519"""));
    }

    [Test]
    public void NoKeyPathMeansNoIdentityArgument()
    {
        using ResolvedSshCredential credential = new("alice");

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).IdentityArgument, Is.Empty);
    }

    [Test]
    public void TheDestinationCombinesUsernameAndHost()
    {
        using ResolvedSshCredential credential = new("alice");

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).Destination,
            Is.EqualTo("alice@example-host"));
    }

    [Test]
    public void AnEmptyUsernameYieldsABareHostDestination()
    {
        using ResolvedSshCredential credential = new(string.Empty);

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).Destination, Is.EqualTo(Host));
    }

    [Test]
    public void TheDestinationUsesTheQualifiedUsername()
    {
        using ResolvedSshCredential credential = new(@"CORP\alice", unqualifiedUsername: "alice");

        Assert.That(OpenSshArgsAdapter.Translate(credential, Host).Destination,
            Is.EqualTo(@"CORP\alice@example-host"));
    }

    // ---- argument validation ------------------------------------------------------

    [Test]
    public void ANullCredentialIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => OpenSshArgsAdapter.Translate(null!, Host));
    }

    [Test]
    public void ANullHostnameIsRejected()
    {
        using ResolvedSshCredential credential = new("alice");

        Assert.Throws<ArgumentNullException>(() => OpenSshArgsAdapter.Translate(credential, null!));
    }
}