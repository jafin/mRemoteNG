using System.Runtime.Versioning;

namespace mRemoteNG.Security.Ssh.Agent
{
    /// <summary>
    /// Reads the effective SSH agent setting.
    /// </summary>
    /// <remarks>
    /// A single application-wide switch, with no per-connection override — see design.md D10. An
    /// SSH agent is a user-wide facility, and neither PuTTY nor <c>ssh.exe</c> exposes a
    /// per-connection agent toggle, so a per-host control would have no counterpart in the tools
    /// mRemoteNG wraps.
    ///
    /// Behind an interface purely so callers can be tested without touching the settings singleton.
    /// </remarks>
    public interface ISshAgentSettings
    {
        /// <summary>Whether SSH.NET-backed callers should consult an SSH agent.</summary>
        bool IsEnabled { get; }
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public sealed class SshAgentSettings : ISshAgentSettings
    {
        public static ISshAgentSettings Default { get; } = new SshAgentSettings();

        public bool IsEnabled => Properties.OptionsCredentialsPage.Default.UseSshAgent;
    }
}
