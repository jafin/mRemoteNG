namespace mRemoteNG.Security.Ssh
{
    /// <summary>
    /// Which default private key format to auto-discover when a connection configures neither a
    /// key path nor a password.
    /// </summary>
    /// <remarks>
    /// The two SSH backends genuinely need different answers, and the difference is intentional
    /// rather than an inconsistency to be flattened:
    /// PuTTY can only load <c>.ppk</c> reliably across versions, whereas <c>ssh.exe</c> wants
    /// OpenSSH-format keys and cannot read <c>.ppk</c> at all.
    /// </remarks>
    public enum DefaultKeyDiscoveryMode
    {
        /// <summary>Do not auto-discover a key.</summary>
        None = 0,

        /// <summary>Discover PuTTY-native <c>.ppk</c> keys only. Used by the PuTTY backend.</summary>
        PuttyPpk = 1,

        /// <summary>Discover OpenSSH-format keys only. Used by the OpenSSH backend.</summary>
        OpenSsh = 2,
    }

    /// <summary>
    /// Per-call inputs to <see cref="ISshCredentialResolver.Resolve"/> that depend on the calling
    /// backend rather than on the connection.
    /// </summary>
    /// <param name="DefaultKeyDiscovery">Which key format to auto-discover, if any.</param>
    /// <param name="ConsultAgent">
    /// Whether to enumerate SSH agent identities. Only SSH.NET-backed callers need this: PuTTY
    /// talks to Pageant natively and <c>ssh.exe</c> talks to the Windows agent natively, so for
    /// those backends the agent is consulted only to decide whether to emit a key argument.
    /// </param>
    public readonly record struct SshCredentialResolutionOptions(
        DefaultKeyDiscoveryMode DefaultKeyDiscovery,
        bool ConsultAgent)
    {
        /// <summary>Discovery and agent both disabled — the conservative default.</summary>
        public static SshCredentialResolutionOptions None { get; } =
            new(DefaultKeyDiscoveryMode.None, ConsultAgent: false);

        /// <summary>Defaults matching the PuTTY backend's current behaviour.</summary>
        public static SshCredentialResolutionOptions ForPutty { get; } =
            new(DefaultKeyDiscoveryMode.PuttyPpk, ConsultAgent: false);

        /// <summary>Defaults matching the OpenSSH backend's current behaviour.</summary>
        public static SshCredentialResolutionOptions ForOpenSsh { get; } =
            new(DefaultKeyDiscoveryMode.OpenSsh, ConsultAgent: false);

        /// <summary>
        /// Defaults for SSH.NET-backed callers, honouring the global agent setting.
        /// </summary>
        /// <remarks>
        /// Only this backend consults the agent through <c>ISshAgentProvider</c>. PuTTY talks to
        /// Pageant natively and <c>ssh.exe</c> talks to the Windows agent natively, so
        /// <see cref="ForPutty"/> and <see cref="ForOpenSsh"/> leave <c>ConsultAgent</c> off — the
        /// setting must not appear to disable those backends' own agent support, which mRemoteNG
        /// does not control.
        /// </remarks>
        public static SshCredentialResolutionOptions ForSshNet(bool agentEnabled) =>
            new(DefaultKeyDiscoveryMode.OpenSsh, ConsultAgent: agentEnabled);
    }
}
