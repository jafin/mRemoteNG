using System;
using System.Collections.Generic;

namespace mRemoteNG.Security.Ssh.Adapters;

/// <summary>
/// The credential portion of an <c>ssh.exe</c> command line, plus anything the backend could
/// not honour.
/// </summary>
/// <param name="IdentityArgument">The <c>-i "path"</c> fragment, or empty.</param>
/// <param name="Destination">The <c>user@host</c> (or bare host) destination.</param>
/// <param name="Unsupported">
/// Credential components <c>ssh.exe</c> cannot accept. Reported rather than dropped silently.
/// </param>
public sealed record OpenSshCredentialArguments(
    string IdentityArgument,
    string Destination,
    IReadOnlyList<SshCredentialDiagnostic> Unsupported);

/// <summary>
/// Translates a <see cref="ResolvedSshCredential"/> into <c>ssh.exe</c> arguments.
/// </summary>
/// <remarks>
/// <c>ssh.exe</c> has no equivalent of PuTTY's <c>-pw</c> and cannot accept a password
/// non-interactively, so a resolved secret cannot be honoured. Today that is silently dropped:
/// a user with a Delinea- or LAPS-backed OpenSSH connection gets an unexplained interactive
/// password prompt from the console and no indication why. This adapter reports it instead. The
/// connection still proceeds — the behaviour is unchanged, the diagnostic is what is new.
///
/// A future <c>SSH_ASKPASS</c> helper could make passwords work here; Win32-OpenSSH support for
/// it is unverified. See design.md D2 and Open Questions.
/// </remarks>
public static class OpenSshArgsAdapter
{
    public static OpenSshCredentialArguments Translate(ResolvedSshCredential credential, string hostname)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(hostname);

        List<SshCredentialDiagnostic> unsupported = [];

        if (credential.HasSecret)
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                SshCredentialDiagnosticSeverity.Error,
                BuildUnsupportedSecretMessage(credential)));
        }

        if (credential.HasKeyMaterial)
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                SshCredentialDiagnosticSeverity.Error,
                $"The OpenSSH backend cannot use the private key supplied by {DescribeSource(credential)}: " +
                "it accepts a key only as a file path. The connection will continue without it."));
        }

        string identity = string.IsNullOrEmpty(credential.PrivateKeyPath)
            ? string.Empty
            : $"-i \"{credential.PrivateKeyPath}\"";

        string destination = string.IsNullOrEmpty(credential.EffectiveUsername)
            ? hostname
            : $"{credential.EffectiveUsername}@{hostname}";

        return new OpenSshCredentialArguments(identity, destination, unsupported);
    }

    private static string BuildUnsupportedSecretMessage(ResolvedSshCredential credential) =>
        $"The OpenSSH backend cannot use the password supplied by {DescribeSource(credential)}: " +
        "ssh.exe has no option to accept a password non-interactively. The connection will " +
        "continue and ssh.exe may prompt for authentication. Use a private key or an SSH agent " +
        "to authenticate this connection without a prompt.";

    private static string DescribeSource(ResolvedSshCredential credential) =>
        credential.Provenance == Connection.ExternalCredentialProvider.None
            ? "this connection"
            : credential.Provenance.ToString();
}