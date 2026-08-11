using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;

namespace mRemoteNG.Connection.Protocol;

public enum ProtocolType
{
    [LocalizedAttributes.LocalizedDescription(nameof(Language.Rdp))]
    RDP = 0,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Vnc))]
    VNC = 1,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.SshV1))]
    SSH1 = 2,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.SshV2))]
    SSH2 = 3,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Telnet))]
    Telnet = 4,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Rlogin))]
    Rlogin = 5,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Raw))]
    RAW = 6,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Http))]
    HTTP = 7,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Https))]
    HTTPS = 8,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Serial))]
    Serial = 9,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.PowerShell))]
    PowerShell = 10,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Ard))]
    ARD = 11,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Terminal))]
    Terminal = 12,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Wsl))]
    WSL = 13,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.AnyDesk))]
    AnyDesk = 14,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Vmrc))]
    VMRC = 15,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Msra))]
    MSRA = 16,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.ExternalTool))]
    IntApp = 20,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.OpenSsh))]
    OpenSSH = 22,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.Winbox))]
    Winbox = 21,

    /// <summary>
    /// SSH hosted in-process — SSH.NET for the transport, xterm.js for the emulator — rather than
    /// a reparented PuTTY window. Deliberately a separate protocol from <see cref="SSH2"/> so both
    /// can be run side by side and chosen per connection; see the add-native-ssh-terminal change,
    /// design.md D1.
    /// </summary>
    [LocalizedAttributes.LocalizedDescription(nameof(Language.SshNative))]
    SSHNative = 23
}

public static class ProtocolFeature
{
    public static bool SupportBlankHostname(ProtocolType protocolType)
    {
        return (protocolType == ProtocolType.IntApp || protocolType == ProtocolType.PowerShell || protocolType == ProtocolType.WSL || protocolType == ProtocolType.Terminal);
    }

    /// <summary>
    /// Whether the SFTP file manager can serve a connection of this protocol.
    /// </summary>
    /// <remarks>
    /// The file manager opens its own SSH.NET connection rather than reusing the session's, so what
    /// matters is whether SSH.NET's SFTP can talk to the host — not how the session itself is
    /// hosted.
    /// <para>
    /// Stated here rather than inline at the menu because it was inline, in two places, when
    /// <see cref="ProtocolType.SSHNative"/> was added by a different change — so the native SSH
    /// terminal shipped with the file manager greyed out for it.
    /// </para>
    /// </remarks>
    public static bool SupportsSftp(ProtocolType protocolType) =>
        ReachableBySshNet(protocolType);

    /// <summary>
    /// Whether the SSH file transfer window can serve a connection of this protocol.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same set as <see cref="SupportsSftp"/>, and named separately because it answers a
    /// different question: the transfer window offers SCP as well as SFTP. Both go through SSH.NET's
    /// own clients, so the constraint is identical — but calling <see cref="SupportsSftp"/> from the
    /// SCP path would read as a mistake to the next person. Sharing the list is what matters, and
    /// that is <see cref="ReachableBySshNet"/>.
    /// </para>
    /// <para>
    /// This narrows as well as widens. The gate was <c>SSH1 | SSH2</c>, inline at two call sites, so
    /// the window was offered for <see cref="ProtocolType.SSH1"/> — which SSH.NET cannot connect at
    /// all, making the entry a route to a failure rather than a transfer — and withheld from
    /// <see cref="ProtocolType.OpenSSH"/> and <see cref="ProtocolType.SSHNative"/>, which it can.
    /// </para>
    /// </remarks>
    public static bool SupportsFileTransfer(ProtocolType protocolType) =>
        ReachableBySshNet(protocolType);

    /// <summary>
    /// The protocols whose host SSH.NET can open a transport to.
    /// </summary>
    /// <remarks>
    /// SSH.NET speaks SSH2 only, so <see cref="ProtocolType.SSH1"/> is out whatever the feature.
    /// How a session is hosted does not enter into it: every feature stated in terms of this opens
    /// its own connection rather than reusing the session's transport, which is what makes a
    /// PuTTY-backed <see cref="ProtocolType.SSH2"/>, an <see cref="ProtocolType.OpenSSH"/> and a
    /// <see cref="ProtocolType.SSHNative"/> connection equally reachable.
    /// </remarks>
    private static bool ReachableBySshNet(ProtocolType protocolType) =>
        protocolType is ProtocolType.SSH2 or ProtocolType.OpenSSH or ProtocolType.SSHNative;
}