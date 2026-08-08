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
    /// Whether to include FIDO/<c>sk-*</c> identities. Defaults to <see langword="false"/>: the
    /// agent library sets <c>SshAgentPrivateKey.Key</c> to <see langword="null"/> for these, and
    /// whether SSH.NET tolerates that is unverified.
    /// </param>
    public readonly record struct SshAgentQuery(
        IReadOnlyList<SshAgentKind> Kinds,
        bool IncludeHardwareBacked = false)
    {
        /// <summary>Consult both transports, excluding hardware-backed identities.</summary>
        public static SshAgentQuery Default { get; } =
            new([SshAgentKind.OpenSsh, SshAgentKind.Pageant]);
    }
}
