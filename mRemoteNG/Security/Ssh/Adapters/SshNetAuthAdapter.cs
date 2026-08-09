using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace mRemoteNG.Security.Ssh.Adapters;

/// <summary>
/// SSH.NET authentication built from a <see cref="ResolvedSshCredential"/>, plus anything the
/// backend could not turn into an authentication method.
/// </summary>
/// <remarks>
/// Owns every <see cref="PrivateKeyFile"/> it loaded and every method it created, so disposing
/// this disposes them. It does <b>not</b> own agent-held identities — those belong to the agent
/// and outlive any one connection.
/// </remarks>
public sealed class SshNetAuthentication : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _owned;
    private readonly List<string> _unansweredPrompts;
    private bool _disposed;

    internal SshNetAuthentication(
        string username,
        AuthenticationMethod[] methods,
        IReadOnlyList<SshCredentialDiagnostic> unsupported,
        IReadOnlyList<IDisposable> owned,
        List<string> unansweredPrompts,
        string? keyFileOffered = null)
    {
        KeyFileOffered = keyFileOffered;
        Username = username;
        Methods = methods;
        Unsupported = unsupported;
        _owned = owned;
        _unansweredPrompts = unansweredPrompts;
    }

    /// <summary>The username every method authenticates as.</summary>
    public string Username { get; }

    /// <summary>
    /// The key file that actually became an authentication source, or null if none did.
    /// </summary>
    /// <remarks>
    /// Not the same as the credential's key path. A path that was resolved but could not be loaded
    /// contributed nothing, and reporting it as offered describes a key the server never saw —
    /// which is the class of half-true message that makes a refusal take four rounds to diagnose.
    /// </remarks>
    public string? KeyFileOffered { get; }

    /// <summary>
    /// The authentication methods, in the order SSH.NET should try them: public key first,
    /// then password, then keyboard-interactive.
    /// </summary>
    public AuthenticationMethod[] Methods { get; }

    /// <summary>
    /// Credential components that could not be turned into a method — a key file that is
    /// missing or will not parse, an agent identity with no usable key behind it. Reported so
    /// the user is told why a credential they configured did not take part, rather than seeing
    /// only a generic authentication failure.
    /// </summary>
    public IReadOnlyList<SshCredentialDiagnostic> Unsupported { get; }

    /// <summary>
    /// Keyboard-interactive prompts the server asked that could not be answered — typically a
    /// second factor. Populated during <c>Connect</c>, not during translation, so it is only
    /// meaningful after a connection attempt.
    /// </summary>
    /// <remarks>
    /// This is what turns "authentication failed" into "the server asked for a verification
    /// code and nothing answered it". mRemoteNG has no way to prompt the user from inside an
    /// SSH.NET authentication callback today, so recording the question is all that can be done
    /// here.
    /// </remarks>
    public IReadOnlyList<string> UnansweredPrompts
    {
        get
        {
            lock (_unansweredPrompts)
                return _unansweredPrompts.ToArray();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (AuthenticationMethod method in Methods)
            (method as IDisposable)?.Dispose();

        foreach (IDisposable owned in _owned)
            owned.Dispose();
    }
}

/// <summary>
/// Translates a <see cref="ResolvedSshCredential"/> into SSH.NET authentication methods.
/// </summary>
/// <remarks>
/// <para>
/// SSH.NET is the most capable of the three backends: unlike <c>ssh.exe</c> it can take a
/// password non-interactively, and unlike both external clients it can load a private key from
/// memory, so provider-supplied key <i>material</i> is usable here rather than merely
/// reportable. That asymmetry is the point of the neutral credential model — see design.md D3.
/// </para>
/// <para>
/// The returned methods read the credential lazily, at authentication time. The credential must
/// stay alive and undisposed until the connection has authenticated.
/// </para>
/// </remarks>
public static class SshNetAuthAdapter
{
    public static SshNetAuthentication Translate(ResolvedSshCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        string username = credential.EffectiveUsername;
        List<SshCredentialDiagnostic> unsupported = [];
        List<IPrivateKeySource> keySources = [];
        List<IDisposable> owned = [];

        // SSH.NET's AuthenticationMethod constructor throws on an empty username, and every caller
        // treats translation as total - so without this a connection missing a username fails as an
        // unhandled exception out of a credential adapter rather than as the setting it is.
        if (string.IsNullOrWhiteSpace(username))
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                SshCredentialDiagnosticSeverity.Error,
                "This connection has no username, so nothing can be authenticated. " +
                "Set a username on the connection or its folder."));

            return new SshNetAuthentication(username, [], unsupported, [], []);
        }

        CollectAgentIdentities(credential, keySources, unsupported);
        string? keyFileOffered = CollectKeyFile(credential, keySources, owned, unsupported);
        CollectKeyMaterial(credential, keySources, owned, unsupported);

        List<AuthenticationMethod> methods = [];

        // One method carrying every source: SSH.NET offers them in turn within a single
        // publickey exchange, which is what `ssh -i key` plus a loaded agent already does.
        if (keySources.Count > 0)
            methods.Add(new PrivateKeyAuthenticationMethod(username, [.. keySources]));

        if (credential.HasSecret)
            methods.Add(new PasswordAuthenticationMethod(username, EncodeSecret(credential)));

        // Always offered, even with no secret to answer with. A server configured for
        // keyboard-interactive only will not accept "password", and a server that requires a
        // second factor advertises it here — without this method the attempt fails before the
        // prompt is ever seen, which is precisely the case worth diagnosing.
        List<string> unansweredPrompts = [];
        KeyboardInteractiveAuthenticationMethod keyboardInteractive = new(username);
        keyboardInteractive.AuthenticationPrompt += (_, e) =>
            AnswerPrompts(e, credential, unansweredPrompts);
        methods.Add(keyboardInteractive);

        return new SshNetAuthentication(
            username, [.. methods], unsupported, owned, unansweredPrompts, keyFileOffered);
    }

    /// <summary>
    /// Answers the prompts a server sends during keyboard-interactive authentication, and
    /// records the ones it cannot.
    /// </summary>
    /// <remarks>
    /// Matching on the prompt text is a heuristic, but it is the only signal the protocol gives:
    /// the server sends free-form strings and the client is expected to show them to a human.
    /// Answering a prompt that merely looks like a password prompt is the established behaviour
    /// for non-interactive SSH clients; answering <i>every</i> prompt with the password would
    /// send it to a second-factor challenge, which is worse than failing.
    /// </remarks>
    internal static void AnswerPrompts(
        AuthenticationPromptEventArgs promptEvent,
        ResolvedSshCredential credential,
        List<string> unansweredPrompts)
    {
        foreach (AuthenticationPrompt prompt in promptEvent.Prompts)
        {
            bool looksLikePassword =
                prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                prompt.Request.Contains("passphrase", StringComparison.OrdinalIgnoreCase);

            if (looksLikePassword && credential.HasSecret)
            {
                prompt.Response = credential.RevealSecret();
                continue;
            }

            lock (unansweredPrompts)
                unansweredPrompts.Add(prompt.Request);
        }
    }

    private static void CollectAgentIdentities(
        ResolvedSshCredential credential,
        List<IPrivateKeySource> keySources,
        List<SshCredentialDiagnostic> unsupported)
    {
        foreach (SshAgentIdentity identity in credential.AgentIdentities)
        {
            if (identity.KeyHandle is IPrivateKeySource source)
            {
                keySources.Add(source);
                continue;
            }

            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                SshCredentialDiagnosticSeverity.Information,
                $"SSH agent identity '{Describe(identity)}' cannot be offered for " +
                "authentication: no usable key was returned with it."));
        }
    }

    private static string? CollectKeyFile(
        ResolvedSshCredential credential,
        List<IPrivateKeySource> keySources,
        List<IDisposable> owned,
        List<SshCredentialDiagnostic> unsupported)
    {
        if (credential.PrivateKeyPath is not { } path)
            return null;

        // A key the user chose failing is an error they must act on. A key discovery went looking
        // for failing is usually not actionable and not theirs - a passphrase on ~/.ssh/id_rsa is
        // that key's ordinary state - and reporting it as an error makes a connection that
        // succeeds by other means look failed.
        bool discovered = credential.KeyPathOrigin == SshKeyPathOrigin.Discovered;
        SshCredentialDiagnosticSeverity severity = discovered
            ? SshCredentialDiagnosticSeverity.Information
            : SshCredentialDiagnosticSeverity.Error;

        if (!File.Exists(path))
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                severity,
                discovered
                    ? $"A private key found by default discovery has since gone: {path}"
                    : $"The configured private key file was not found: {path}"));
            return null;
        }

        try
        {
            PrivateKeyFile keyFile = LoadKeyFile(path, credential);
            keySources.Add(keyFile);
            owned.Add(keyFile);
            return path;
        }
        catch (Exception ex)
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                severity,
                discovered
                    ? $"The private key {path}, found by default discovery, was not used: {ex.Message} " +
                      "Set it on the connection if you want it offered."
                    : $"The private key file {path} could not be loaded: {ex.Message}"));
        }

        return null;
    }

    private static PrivateKeyFile LoadKeyFile(string path, ResolvedSshCredential credential)
    {
        try
        {
            return new PrivateKeyFile(path);
        }
        catch (SshPassPhraseNullOrEmptyException) when (credential.HasSecret)
        {
            // The key is encrypted and the connection carries a secret. That secret is far more
            // often the key's passphrase than a separate account password — a connection that
            // authenticates by key does not usually also store one — so it is worth one retry.
            // If it is the wrong passphrase this throws again and the caller reports it.
            return new PrivateKeyFile(path, credential.RevealSecret());
        }
    }

    private static void CollectKeyMaterial(
        ResolvedSshCredential credential,
        List<IPrivateKeySource> keySources,
        List<IDisposable> owned,
        List<SshCredentialDiagnostic> unsupported)
    {
        if (!credential.HasKeyMaterial)
            return;

        try
        {
            using MemoryStream material = new(Encoding.UTF8.GetBytes(credential.RevealKeyMaterial()));
            PrivateKeyFile keyFile = credential.HasSecret
                ? new PrivateKeyFile(material, credential.RevealSecret())
                : new PrivateKeyFile(material);

            keySources.Add(keyFile);
            owned.Add(keyFile);
        }
        catch (Exception ex)
        {
            unsupported.Add(new SshCredentialDiagnostic(
                credential.Provenance,
                SshCredentialDiagnosticSeverity.Error,
                $"The private key supplied by {DescribeSource(credential)} could not be " +
                $"parsed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Encodes the secret without materialising it as a <see cref="string"/>. SSH.NET's
    /// <see cref="byte"/> array overload is used for exactly this reason.
    /// </summary>
    private static byte[] EncodeSecret(ResolvedSshCredential credential)
    {
        ReadOnlySpan<char> secret = credential.SecretSpan;
        byte[] encoded = new byte[Encoding.UTF8.GetByteCount(secret)];
        Encoding.UTF8.GetBytes(secret, encoded);
        return encoded;
    }

    private static string Describe(SshAgentIdentity identity) =>
        string.IsNullOrEmpty(identity.Comment) ? identity.Algorithm : identity.Comment;

    private static string DescribeSource(ResolvedSshCredential credential) =>
        credential.Provenance == Connection.ExternalCredentialProvider.None
            ? "this connection"
            : credential.Provenance.ToString();
}