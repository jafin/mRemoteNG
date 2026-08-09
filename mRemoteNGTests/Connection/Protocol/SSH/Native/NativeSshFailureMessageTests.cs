using System.Collections.Generic;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.SSH.Native;
using mRemoteNG.Security.Ssh;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

/// <summary>
/// What the user is told when a connection fails.
/// </summary>
/// <remarks>
/// Found by actually connecting: a mistyped or passphrase-encrypted key produces
/// "Permission denied (keyboard-interactive)", which is true, useless, and identical for both
/// causes. Credential resolution already knew why, but only said so on a channel nobody reads
/// when a tab fails to open.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class NativeSshFailureMessageTests
{
    private static SshCredentialDiagnostic Diagnostic(string message, SshCredentialDiagnosticSeverity severity) =>
        new(ExternalCredentialProvider.None, severity, message);

    private const string Denied = "Permission denied (keyboard-interactive).";

    [Test]
    public void WithoutDiagnosticsTheMessageIsUnchanged()
    {
        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, null), Is.EqualTo(Denied));
        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, []), Is.EqualTo(Denied));
    }

    [Test]
    public void AMissingKeyFileIsNamedInTheFailure()
    {
        List<SshCredentialDiagnostic> diagnostics =
        [
            Diagnostic("The configured private key file was not found: C:\\keys\\id_ed25519",
                SshCredentialDiagnosticSeverity.Error)
        ];

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, diagnostics),
            Is.EqualTo($"{Denied} The configured private key file was not found: C:\\keys\\id_ed25519"));
    }

    [Test]
    public void AnEncryptedKeyIsNamedInTheFailure()
    {
        // The real case: no key configured, so discovery fell back to ~/.ssh/id_rsa, which is
        // passphrase-encrypted and therefore silently contributed nothing.
        List<SshCredentialDiagnostic> diagnostics =
        [
            Diagnostic("The private key file C:\\Users\\x\\.ssh\\id_rsa could not be loaded: Private key is encrypted but passphrase is empty.",
                SshCredentialDiagnosticSeverity.Error)
        ];

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, diagnostics),
            Does.Contain("passphrase is empty"));
    }

    [Test]
    public void InformationalDiagnosticsAreLeftOut()
    {
        // These narrate what worked. Repeating them would bury the one line that explains the
        // failure, which is the whole point of the exercise.
        List<SshCredentialDiagnostic> diagnostics =
        [
            Diagnostic("Using the credential from the vault.", SshCredentialDiagnosticSeverity.Information),
            Diagnostic("Agent consulted.", SshCredentialDiagnosticSeverity.Information)
        ];

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, diagnostics), Is.EqualTo(Denied));
    }

    [Test]
    public void ProtocolErrorsAreIncludedToo()
    {
        List<SshCredentialDiagnostic> diagnostics =
        [
            Diagnostic("The credential provider returned nothing.", SshCredentialDiagnosticSeverity.ProtocolError)
        ];

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, diagnostics),
            Does.Contain("returned nothing"));
    }

    [Test]
    public void RepeatedDiagnosticsAreNotRepeatedInTheMessage()
    {
        List<SshCredentialDiagnostic> diagnostics =
        [
            Diagnostic("Key not found.", SshCredentialDiagnosticSeverity.Error),
            Diagnostic("Key not found.", SshCredentialDiagnosticSeverity.Error)
        ];

        Assert.That(ProtocolNativeSsh.DescribeFailure(Denied, diagnostics),
            Is.EqualTo($"{Denied} Key not found."));
    }
}
