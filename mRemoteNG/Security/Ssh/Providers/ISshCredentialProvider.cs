using System.Collections.Generic;
using mRemoteNG.Connection;

namespace mRemoteNG.Security.Ssh.Providers;

/// <summary>
/// Everything a credential provider may need in order to answer, gathered from the connection
/// so that providers never touch <c>ConnectionInfo</c> or <c>InterfaceControl</c> directly.
/// </summary>
/// <param name="UserViaApi">The provider-side lookup key (<c>ConnectionInfo.UserViaAPI</c>).</param>
/// <param name="Username">The connection's configured username, before any fallback.</param>
/// <param name="Hostname">The connection's hostname. Used by LAPS and Vault SSH-OTP.</param>
/// <param name="VaultMount">Vault/OpenBao mount path.</param>
/// <param name="VaultRole">Vault/OpenBao role.</param>
/// <param name="VaultSecretEngine">Which Vault/OpenBao engine to use.</param>
public readonly record struct SshProviderRequest(
    string UserViaApi,
    string Username,
    string Hostname,
    string VaultMount,
    string VaultRole,
    VaultOpenbaoSecretEngine VaultSecretEngine);

/// <summary>
/// What a provider returned. Any of the credential fields may be empty: providers overwrite
/// only what they know.
/// </summary>
public sealed record SshProviderResult(
    string Username,
    string Password,
    string PrivateKey,
    IReadOnlyList<SshCredentialDiagnostic> Diagnostics)
{
    private static readonly IReadOnlyList<SshCredentialDiagnostic> NoDiagnostics = [];

    /// <summary>
    /// Whether the provider returned normally.
    /// </summary>
    /// <remarks>
    /// This distinction is load-bearing for behaviour preservation. Today the providers are
    /// invoked as <c>Fetch(key, out username, out password, out _, out privateKey)</c> against
    /// the caller's own locals. On success the locals are overwritten unconditionally — even
    /// with empty strings. On an exception they keep whatever they already held, which is the
    /// connection's configured credentials. A resolver that copied the result fields
    /// unconditionally would wipe the configured username and password whenever a provider
    /// failed. So: copy on success, keep the originals on failure.
    /// </remarks>
    public bool Succeeded { get; init; } = true;

    public static SshProviderResult Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, NoDiagnostics);

    public static SshProviderResult Failed(SshCredentialDiagnostic diagnostic) =>
        new(string.Empty, string.Empty, string.Empty, [diagnostic]) { Succeeded = false };

    public static SshProviderResult Failed(params SshCredentialDiagnostic[] diagnostics) =>
        new(string.Empty, string.Empty, string.Empty, diagnostics) { Succeeded = false };
}

/// <summary>
/// A single external credential source.
/// </summary>
/// <remarks>
/// Implementations are thin wrappers over the existing static interfaces in
/// <c>ExternalConnectors</c>. They exist so the resolver is testable without a live Delinea,
/// Passwordstate, 1Password, PasswordSafe, Vault or LAPS instance — the reason task 1.2 could
/// not be done in situ. Each wrapper reproduces its provider's current exception handling and
/// message text exactly; normalising those is a behaviour change and is not in scope here.
/// </remarks>
public interface ISshCredentialProvider
{
    /// <summary>Which provider this implementation serves.</summary>
    ExternalCredentialProvider Kind { get; }

    /// <summary>
    /// Fetches credentials. MUST NOT throw: provider failure is reported as a diagnostic on the
    /// result so resolution can continue with the remaining sources.
    /// </summary>
    SshProviderResult Fetch(SshProviderRequest request);
}