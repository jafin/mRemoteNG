using System;

namespace mRemoteNG.Security.Ssh
{
    /// <summary>
    /// A single identity offered by an SSH agent (OpenSSH agent or PuTTY Pageant).
    /// </summary>
    /// <remarks>
    /// Deliberately free of any SSH.NET or agent-library types: this is part of the
    /// backend-neutral credential model, and every SSH backend must be able to reason
    /// about it. The agent implementation that populates it arrives with
    /// <c>ISshAgentProvider</c>.
    ///
    /// Only the public half of the key is ever represented here. Agent-held private keys
    /// never leave the agent — signing is delegated to it.
    /// </remarks>
    /// <param name="Comment">The agent's label for the identity, typically <c>user@host</c>.</param>
    /// <param name="Algorithm">The key algorithm, e.g. <c>ssh-ed25519</c> or <c>sk-ssh-ed25519@openssh.com</c>.</param>
    public sealed record SshAgentIdentity(string Comment, string Algorithm)
    {
        /// <summary>
        /// Whether this is a FIDO/hardware-backed identity (<c>sk-*</c>). These are currently
        /// filtered out before reaching SSH.NET; see the agent provider for the reason.
        /// </summary>
        public bool IsHardwareBacked =>
            Algorithm.StartsWith("sk-", StringComparison.Ordinal);
    }
}
