using System.Collections.Generic;

namespace mRemoteNG.Security.Ssh.Agent
{
    /// <summary>
    /// Which agent transports to consult.
    /// </summary>
    public enum SshAgentKind
    {
        /// <summary>The Windows OpenSSH agent, over its named pipe.</summary>
        OpenSsh = 0,

        /// <summary>PuTTY Pageant.</summary>
        Pageant = 1,
    }

    /// <summary>
    /// Enumerates identities held by a running SSH agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps the third-party agent library behind an mRemoteNG-owned interface so the dependency is
    /// swappable and so the resolver is testable without a live agent — no test may require one.
    /// </para>
    /// <para>
    /// Only SSH.NET-backed callers need identities. PuTTY talks to Pageant natively and
    /// <c>ssh.exe</c> talks to the Windows agent natively; for those backends the agent is consulted
    /// only to decide whether emitting a key argument is necessary at all.
    /// </para>
    /// </remarks>
    public interface ISshAgentProvider
    {
        /// <summary>
        /// Returns the identities the agent is offering.
        /// </summary>
        /// <returns>
        /// The usable identities, or an empty list. MUST NOT throw and MUST NOT return
        /// <see langword="null"/>: an absent, unreachable or misbehaving agent is not a connection
        /// failure, it just means resolution continues with the remaining credential sources.
        /// </returns>
        IReadOnlyList<SshAgentIdentity> GetIdentities(SshAgentQuery query);
    }

    /// <summary>
    /// What to ask the agent for.
    /// </summary>
    /// <param name="Kinds">Transports to try, in order. Results are concatenated, first-seen wins.</param>
    /// <param name="IncludeHardwareBacked">
    /// Whether to include FIDO/<c>sk-*</c> identities. Defaults to <see langword="true"/>.
    /// <para>
    /// This defaulted to <see langword="false"/> while it was unverified whether SSH.NET faults on
    /// <c>SshAgentPrivateKey.Key</c> being <see langword="null"/>, which the agent library sets for
    /// these keys. It cannot: <c>Key</c> is not a member of <c>IPrivateKeySource</c>, the only
    /// surface SSH.NET consumes, and the pinned agent library carries the public-key blob and the
    /// agent's signature straight through for <c>sk-*</c> keys instead. Both facts are pinned by
    /// tests in <c>SshNetAuthAdapterTests</c>, so this flips back if either stops holding.
    /// </para>
    /// <para>
    /// Excluding them is still available, but it is the wrong default: a user whose only credential
    /// is a security key would be offered nothing at all.
    /// </para>
    /// </param>
    public readonly record struct SshAgentQuery(
        IReadOnlyList<SshAgentKind> Kinds,
        bool IncludeHardwareBacked = true)
    {
        /// <summary>Consult both transports, including hardware-backed identities.</summary>
        public static SshAgentQuery Default { get; } =
            new([SshAgentKind.OpenSsh, SshAgentKind.Pageant]);
    }
}
