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

        /// <summary>
        /// The agent library's object for this identity, carried opaquely so the SSH.NET adapter
        /// can authenticate with the exact identity that was enumerated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Weakly typed on purpose. The alternative — declaring this as
        /// <c>Renci.SshNet.IPrivateKeySource</c> — would put an SSH.NET type on the record that the
        /// PuTTY and OpenSSH adapters also consume, which is exactly the coupling this model exists
        /// to avoid. Only <c>SshNetAuthAdapter</c> unboxes it; every other consumer treats an
        /// identity as the descriptive <see cref="Comment"/>/<see cref="Algorithm"/> pair it
        /// appears to be.
        /// </para>
        /// <para>
        /// The alternative to carrying it at all is re-querying the agent when the SSH.NET
        /// authentication methods are built. That costs a second round trip per connection and,
        /// worse, lets the identities we reported diverge from the ones we actually offer if a key
        /// is added or removed in between.
        /// </para>
        /// <para>
        /// It participates in record equality, so two identities describing the same key from
        /// different agent sessions are not equal. Nothing relies on cross-session equality; the
        /// provider de-duplicates on algorithm and comment rather than on the whole record.
        /// </para>
        /// </remarks>
        internal object? KeyHandle { get; init; }
    }
}
