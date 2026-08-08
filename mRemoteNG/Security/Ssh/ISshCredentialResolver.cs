using mRemoteNG.Connection;

namespace mRemoteNG.Security.Ssh
{
    /// <summary>
    /// Resolves an mRemoteNG connection into SSH credentials, consolidating the external
    /// credential providers, empty-credential fallback, domain qualification, and default-key
    /// discovery that every SSH backend would otherwise reimplement.
    /// </summary>
    /// <remarks>
    /// <para><b>Resolution is re-invocable and never cached.</b> Every call performs a fresh
    /// resolution, including a fresh call to whichever external provider applies.</para>
    /// <para>
    /// This is a correctness requirement, not a preference. Vault/OpenBao SSH-OTP mints a
    /// <i>single-use</i> credential: a cached result would authenticate the first connection and
    /// fail every subsequent one. Two concurrent connections to the same host — the case that
    /// arrives with the SFTP browser sitting beside a terminal — must each obtain their own
    /// credential, and a retry after a failed attempt must re-mint rather than replay.
    /// </para>
    /// <para>
    /// The cost is that replayable providers (Delinea, Passwordstate, 1Password, PasswordSafe,
    /// LAPS, Vault password engine) are called once per connection attempt rather than once per
    /// connection. That is an extra API round trip and an extra audit entry, which is the correct
    /// trade against silently breaking single-use credentials.
    /// </para>
    /// <para>
    /// Callers own the returned <see cref="ResolvedSshCredential"/> and MUST dispose it once the
    /// connection attempt has consumed it. A credential MUST NOT be retained across attempts.
    /// </para>
    /// </remarks>
    public interface ISshCredentialResolver
    {
        /// <summary>
        /// Resolves credentials for <paramref name="connectionInfo"/>.
        /// </summary>
        /// <param name="connectionInfo">The connection being opened.</param>
        /// <param name="options">Controls key discovery and whether an SSH agent is consulted.</param>
        /// <returns>
        /// A fresh credential. Never <see langword="null"/>: a connection with no usable
        /// credential still yields a credential carrying the effective username and nothing else,
        /// so backends can proceed to interactive authentication.
        /// </returns>
        /// <remarks>
        /// Does not throw for provider failure. A provider that fails is recorded as an error
        /// naming the provider, and resolution continues with the remaining sources.
        /// </remarks>
        ResolvedSshCredential Resolve(ConnectionInfo connectionInfo, SshCredentialResolutionOptions options);
    }
}
