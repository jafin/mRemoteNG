using System;
using System.Collections.Generic;
using mRemoteNG.Connection;

namespace mRemoteNG.Security.Ssh;

/// <summary>
/// The outcome of resolving an mRemoteNG connection into SSH credentials, in a form
/// no SSH backend is privileged by.
/// </summary>
/// <remarks>
/// <para>
/// This type deliberately exposes no PuTTY command-line arguments, no OpenSSH command-line
/// arguments, and no SSH.NET <c>AuthenticationMethod</c> instances. Translation into any of
/// those is an adapter's job. The reason is concrete: <c>ssh.exe</c> cannot accept a password
/// non-interactively, so a credential model shaped around "resolve to a password string I can
/// pass on a command line" is unusable for that backend.
/// </para>
/// <para>
/// <b>Secret lifetime.</b> Secrets are held in <see cref="char"/> buffers that are zeroed on
/// <see cref="Dispose"/>. This bounds the lifetime of <i>this type's copy</i> only. The
/// external credential providers hand back <see cref="string"/>, which is immutable and
/// cannot be scrubbed, so a copy of the secret survives in the managed heap until collected.
/// Reducing that requires changing the provider signatures and is out of scope here.
/// </para>
/// </remarks>
public sealed class ResolvedSshCredential : IDisposable
{
    private static readonly IReadOnlyList<SshAgentIdentity> NoIdentities = [];

    private char[]? _secret;
    private char[]? _keyMaterial;
    private bool _disposed;

    private static readonly IReadOnlyList<SshCredentialDiagnostic> NoDiagnostics = [];

    public ResolvedSshCredential(
        string effectiveUsername,
        string? unqualifiedUsername = null,
        string? secret = null,
        string? keyMaterial = null,
        string? privateKeyPath = null,
        IReadOnlyList<SshAgentIdentity>? agentIdentities = null,
        ExternalCredentialProvider provenance = ExternalCredentialProvider.None,
        IReadOnlyList<SshCredentialDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(effectiveUsername);

        EffectiveUsername = effectiveUsername;
        UnqualifiedUsername = unqualifiedUsername ?? effectiveUsername;
        PrivateKeyPath = string.IsNullOrEmpty(privateKeyPath) ? null : privateKeyPath;
        AgentIdentities = agentIdentities ?? NoIdentities;
        Provenance = provenance;
        Diagnostics = diagnostics ?? NoDiagnostics;

        _secret = string.IsNullOrEmpty(secret) ? null : secret.ToCharArray();
        _keyMaterial = string.IsNullOrEmpty(keyMaterial) ? null : keyMaterial.ToCharArray();
    }

    /// <summary>
    /// Messages raised during resolution, each tagged with the channel the caller must replay
    /// it on. The resolver cannot raise these itself — see <see cref="SshCredentialDiagnostic"/>.
    /// </summary>
    public IReadOnlyList<SshCredentialDiagnostic> Diagnostics { get; }

    /// <summary>
    /// The username to authenticate as, after empty-credential fallback and domain
    /// qualification. Backends use this verbatim and MUST NOT re-apply those rules.
    /// </summary>
    public string EffectiveUsername { get; }

    /// <summary>
    /// The username after fallback but <i>before</i> domain qualification.
    /// </summary>
    /// <remarks>
    /// Almost every consumer wants <see cref="EffectiveUsername"/>. This exists because the
    /// PuTTY Vault/OpenBao SSH-OTP path passes the unqualified name to
    /// <c>vault-ssh-helper-plugin.exe</c> and matches it against the plugin's data request,
    /// while <c>-l</c> on the same command line gets the qualified form. Collapsing the two
    /// would break SSH-OTP for any connection that also sets a domain.
    /// </remarks>
    public string UnqualifiedUsername { get; }

    /// <summary>Path to a private key file on disk, or <see langword="null"/>.</summary>
    public string? PrivateKeyPath { get; }

    /// <summary>Identities offered by an SSH agent. Empty when no agent was consulted or none matched.</summary>
    public IReadOnlyList<SshAgentIdentity> AgentIdentities { get; }

    /// <summary>
    /// Which credential source answered. <see cref="ExternalCredentialProvider.None"/> means the
    /// connection's own inline credentials were used.
    /// </summary>
    public ExternalCredentialProvider Provenance { get; }

    public bool HasSecret => _secret is { Length: > 0 };

    public bool HasKeyMaterial => _keyMaterial is { Length: > 0 };

    public bool HasAgentIdentities => AgentIdentities.Count > 0;

    /// <summary>
    /// Materialises the secret as a <see cref="string"/> for backends that can only accept one
    /// (for example a PuTTY <c>-pw</c> argument). The returned string cannot be scrubbed, so
    /// call this as late as possible and do not retain the result.
    /// </summary>
    public string RevealSecret()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _secret is null ? string.Empty : new string(_secret);
    }

    /// <summary>
    /// Materialises provider-supplied private key material. Same caveat as <see cref="RevealSecret"/>.
    /// </summary>
    public string RevealKeyMaterial()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _keyMaterial is null ? string.Empty : new string(_keyMaterial);
    }

    /// <summary>Non-materialising view of the secret, for consumers that can take a span.</summary>
    public ReadOnlySpan<char> SecretSpan
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _secret is null ? ReadOnlySpan<char>.Empty : _secret;
        }
    }

    // Exposed to mRemoteNGTests (InternalsVisibleTo) so the zeroing contract is observable
    // without reflection. Not part of the public surface.
    internal char[]? SecretBuffer => _secret;

    internal char[]? KeyMaterialBuffer => _keyMaterial;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_secret is not null)
        {
            Array.Clear(_secret);
            _secret = null;
        }

        if (_keyMaterial is not null)
        {
            Array.Clear(_keyMaterial);
            _keyMaterial = null;
        }

        _disposed = true;
    }

    /// <summary>
    /// Provenance only. Never includes the secret or key material — see the resolver's
    /// logging contract.
    /// </summary>
    public override string ToString() =>
        $"{nameof(ResolvedSshCredential)}(user={EffectiveUsername}, provenance={Provenance}, " +
        $"secret={(HasSecret ? "yes" : "no")}, keyMaterial={(HasKeyMaterial ? "yes" : "no")}, " +
        $"keyPath={(PrivateKeyPath is null ? "no" : "yes")}, agentIdentities={AgentIdentities.Count})";
}