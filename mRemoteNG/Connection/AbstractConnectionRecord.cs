using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.Http;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Properties;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tools.Attributes;

namespace mRemoteNG.Connection;

[SupportedOSPlatform("windows")]
public abstract class AbstractConnectionRecord(string uniqueId) : INotifyPropertyChanged
{
    #region Fields

    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _icon = string.Empty;
    private string _panel = string.Empty;
    private string _color = string.Empty;
    private string _tabColor = string.Empty;
    private ConnectionFrameColor _connectionFrameColor;

    private string _hostname = string.Empty;
    private string _ipAddress = string.Empty;
    private ConnectionAddressPrimary _connectionAddressPrimary;
    private string _alternativeAddress = string.Empty;
    private ExternalAddressProvider _externalAddressProvider;
    private string _ec2InstanceId = "";
    private string _ec2Region = "";
    private ExternalCredentialProvider _externalCredentialProvider;
    private string _userViaAPI = "";
    private string _username = string.Empty;
    private SecureString? _password;
    private PendingConnectionSecret? _pendingPassword;
    private string _vaultRole = string.Empty;
    private string _vaultMount = string.Empty;
    private VaultOpenbaoSecretEngine _vaultSecretEngine;
    private string _domain = string.Empty;
    private string _vmId = string.Empty;
    private bool _useEnhancedMode;

    private string _sshTunnelConnectionName = string.Empty;
    private ProtocolType _protocol;
    private RdpVersion _rdpProtocolVersion;
    private string _extApp = string.Empty;
    private int _port;
    private string _sshOptions = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _puttySession = string.Empty;
    private string _httpPath = string.Empty;
    private bool _useConsoleSession;
    private AuthenticationLevel _rdpAuthenticationLevel;
    private int _rdpMinutesToIdleTimeout;
    private bool _rdpAlertIdleTimeout;
    private string _loadBalanceInfo = string.Empty;
    private HTTPBase.RenderingEngine _renderingEngine;
    private bool _scriptErrorsSuppressed = true;
    private bool _usePersistentBrowser;
    private bool _showBrowserNavigationBar;
    private bool _useCredSsp;
    private bool _useRestrictedAdmin;
    private bool _useRCG;
    private bool _useRedirectionServerName;
    private bool _useVmId;

    private RDGatewayUsageMethod _rdGatewayUsageMethod;
    private string _rdGatewayHostname = string.Empty;
    private RDGatewayUseConnectionCredentials _rdGatewayUseConnectionCredentials;
    private string _rdGatewayUsername = string.Empty;
    private SecureString? _rdGatewayPassword;
    private PendingConnectionSecret? _pendingRdGatewayPassword;
    private string _rdGatewayDomain = string.Empty;
    private string _rdGatewayAccessToken = string.Empty;
    private ExternalCredentialProvider _rdGatewayExternalCredentialProvider;
    private string _rdGatewayUserViaAPI = "";


    private RDPResolutions _resolution;
    private RDPSizingMode _rdpSizingMode;
    private int _resolutionWidth;
    private int _resolutionHeight;
    private RDPDesktopScaleFactor _desktopScaleFactor;
    private bool _automaticResize;
    private bool _rdpUseMultimon;
    private RDPColors _colors;
    private bool _cacheBitmaps;
    private bool _displayWallpaper;
    private bool _displayThemes;
    private bool _enableFontSmoothing;
    private bool _enableDesktopComposition;
    private bool _disableFullWindowDrag;
    private bool _disableMenuAnimations;
    private bool _disableCursorShadow;
    private bool _disableCursorBlinking;

    private bool _redirectKeys;
    private RDPDiskDrives _redirectDiskDrives;
    private string _redirectDiskDrivesCustom = string.Empty;
    private bool _redirectPrinters;
    private bool _redirectClipboard;
    private bool _redirectPorts;
    private bool _redirectSmartCards;
    private RDPSounds _redirectSound;
    private RDPSoundQuality _soundQuality;
    private bool _redirectAudioCapture;
    private bool _redirectWebAuthn;
    private bool _enableRdsAadAuth;

    private string _preExtApp = string.Empty;
    private string _postExtApp = string.Empty;
    private string _macAddress = string.Empty;
    private string _openingCommand = string.Empty;
    private string _userField = string.Empty;
    private string _userField1 = string.Empty;
    private string _userField2 = string.Empty;
    private string _userField3 = string.Empty;
    private string _userField4 = string.Empty;
    private string _userField5 = string.Empty;
    private string _userField6 = string.Empty;
    private string _userField7 = string.Empty;
    private string _userField8 = string.Empty;
    private string _userField9 = string.Empty;
    private string _userField10 = string.Empty;
    private string _notes = string.Empty;
    private string _environmentTags = "";
    private string _rdpStartProgram = string.Empty;
    private string _rdpStartProgramWorkDir = string.Empty;
    private string _rdpRemoteAppProgram = string.Empty;
    private string _rdpRemoteAppCmdLine = string.Empty;
    private string _rdpSignScope = string.Empty;
    private string _rdpSignature = string.Empty;
    private bool _favorite;
    private bool _retryOnFirstConnect;
    private bool _waitForIPAvailability;
    private int _waitForIPTimeout = 60;
    private bool _alwaysPromptForCredentials;
    private bool _isTemplate;

    private ProtocolVNC.Compression _vncCompression;
    private ProtocolVNC.Encoding _vncEncoding;
    private ProtocolVNC.AuthMode _vncAuthMode;
    private ProtocolVNC.ProxyType _vncProxyType;
    private string _vncProxyIp = string.Empty;
    private int _vncProxyPort;
    private string _vncProxyUsername = string.Empty;
    private SecureString? _vncProxyPassword;
    private PendingConnectionSecret? _pendingVncProxyPassword;
    private ProtocolVNC.Colors _vncColors;
    private ProtocolVNC.SmartSizeMode _vncSmartSizeMode;
    private bool _vncViewOnly;
    private bool _vncClipboardRedirect = true;

    private string _credentialId = string.Empty;

    #endregion

    #region Properties

    #region Display

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Name)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionName))]
    public virtual string Name
    {
        get => _name;
        set => SetField(ref _name, value, nameof(Name));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Description)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDescription))]
    public virtual string Description
    {
        get => GetPropertyValue(nameof(Description), _description);
        set => SetField(ref _description, value, nameof(Description));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display))]
    [DisplayName("Is Template")]
    [Description("If enabled, this connection serves as a template and cannot be initiated.")]
    public virtual bool IsTemplate
    {
        get => GetPropertyValue(nameof(IsTemplate), _isTemplate);
        set => SetField(ref _isTemplate, value, nameof(IsTemplate));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     TypeConverter(typeof(ConnectionIcon)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Icon)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionIcon))]
    public virtual string Icon
    {
        get => GetPropertyValue(nameof(Icon), _icon);
        set => SetField(ref _icon, value, nameof(Icon));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Panel)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionPanel))]
    public virtual string Panel
    {
        get => GetPropertyValue(nameof(Panel), _panel);
        set => SetField(ref _panel, value, nameof(Panel));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Color)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionColor)),
     Editor(typeof(UI.Controls.ConnectionInfoPropertyGrid.ColorStringEditor), typeof(System.Drawing.Design.UITypeEditor)),
     TypeConverter(typeof(MiscTools.TabColorConverter))]
    public virtual string Color
    {
        get => GetPropertyValue(nameof(Color), _color);
        set => SetField(ref _color, value, nameof(Color));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.TabColor)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionTabColor)),
     Editor(typeof(UI.Controls.ConnectionInfoPropertyGrid.ColorStringEditor), typeof(System.Drawing.Design.UITypeEditor)),
     TypeConverter(typeof(MiscTools.TabColorConverter))]
    public virtual string TabColor
    {
        get => GetPropertyValue(nameof(TabColor), _tabColor);
        set => SetField(ref _tabColor, value, nameof(TabColor));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Display)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ConnectionFrameColor)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionConnectionFrameColor)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter))]
    public virtual ConnectionFrameColor ConnectionFrameColor
    {
        get => GetPropertyValue(nameof(ConnectionFrameColor), _connectionFrameColor);
        set => SetField(ref _connectionFrameColor, value, nameof(ConnectionFrameColor));
    }

    #endregion

    #region Connection

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.HostnameIp)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionHostnameIp)),
     AttributeUsedInAllProtocolsExcept()]
    public virtual string Hostname
    {
        get => GetPropertyValue(nameof(Hostname), GetEffectiveHostname(_hostname));
        set => SetField(ref _hostname, value?.Trim() ?? string.Empty, nameof(Hostname));
    }

    /// <summary>
    /// Returns the effective hostname based on <see cref=nameof(ConnectionAddressPrimary)/> setting,
    /// with <c>%name%</c> tokens expanded.
    /// </summary>
    private string GetEffectiveHostname(string hostname)
    {
        string raw = _connectionAddressPrimary == ConnectionAddressPrimary.IPAddress && !string.IsNullOrWhiteSpace(_ipAddress)
            ? _ipAddress.Trim()
            : hostname?.Trim() ?? string.Empty;
        return ExpandHostnameVariables(raw);
    }

    /// <summary>
    /// Expands <c>%name%</c> tokens in a hostname template to the connection's <see cref=nameof(Name)/> value.
    /// Intentionally does not expand <c>%hostname%</c> to avoid circular references.
    /// </summary>
    private string ExpandHostnameVariables(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.Contains('%'))
            return raw;
        return raw.Replace("%name%", _name, StringComparison.OrdinalIgnoreCase);
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     DisplayName("IP Address"),
     Description("IP address for this connection. When 'Primary Address' is set to 'IP Address', this is used for connecting instead of the Hostname field."),
     AttributeUsedInAllProtocolsExcept()]
    public virtual string IPAddress
    {
        get => GetPropertyValue(nameof(IPAddress), _ipAddress?.Trim() ?? string.Empty);
        set => SetField(ref _ipAddress, value?.Trim() ?? string.Empty, nameof(IPAddress));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     DisplayName("Primary Address"),
     Description("Determines which address field (Hostname or IP Address) is used when initiating a connection. Defaults to Hostname for backward compatibility."),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInAllProtocolsExcept()]
    public virtual ConnectionAddressPrimary ConnectionAddressPrimary
    {
        get => GetPropertyValue(nameof(ConnectionAddressPrimary), _connectionAddressPrimary);
        set => SetField(ref _connectionAddressPrimary, value, nameof(ConnectionAddressPrimary));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     DisplayName("Alternative Hostname/IP"),
     Description("Optional alternate hostname or IP address used when connecting with options."),
     AttributeUsedInAllProtocolsExcept()]
    public virtual string AlternativeAddress
    {
        get => GetPropertyValue(nameof(AlternativeAddress), _alternativeAddress?.Trim() ?? string.Empty);
        set => SetField(ref _alternativeAddress, value?.Trim() ?? string.Empty, nameof(AlternativeAddress));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Port)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionPort)),
     AttributeUsedInAllProtocolsExcept(ProtocolType.MSRA)]
    public virtual int Port
    {
        get => GetPropertyValue(nameof(Port), _port);
        set => SetField(ref _port, value, nameof(Port));
    }

    // external credential provider selector
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalCredentialProvider)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalCredentialProvider)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public ExternalCredentialProvider ExternalCredentialProvider
    {
        get => GetPropertyValue(nameof(ExternalCredentialProvider), _externalCredentialProvider);
        set => SetField(ref _externalCredentialProvider, value, nameof(ExternalCredentialProvider));
    }

    [Browsable(false)]
    public virtual string CredentialId
    {
        get => GetPropertyValue(nameof(CredentialId), _credentialId);
        set => SetField(ref _credentialId, value, nameof(CredentialId));
    }

    // credential record identifier for external credential provider
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UserViaAPI)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUserViaAPI)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public virtual string UserViaAPI
    {
        get => GetPropertyValue(nameof(UserViaAPI), _userViaAPI);
        set => SetField(ref _userViaAPI, value, nameof(UserViaAPI));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Username)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUsername)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.OpenSSH, ProtocolType.HTTP, ProtocolType.HTTPS, ProtocolType.IntApp, ProtocolType.Winbox, ProtocolType.VMRC, ProtocolType.SSHNative)]
    public virtual string Username
    {
        get => GetPropertyValue(nameof(Username), _username);
        set => SetField(ref _username, Settings.Default.DoNotTrimUsername ? value : (value?.Trim() ?? string.Empty), nameof(Username));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Password)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionPassword)),
     PasswordPropertyText(true),
     Editor(typeof(UI.Controls.ConnectionInfoPropertyGrid.PasswordRevealEditor), typeof(UITypeEditor)),
     AttributeUsedInAllProtocolsExcept(ProtocolType.Telnet, ProtocolType.Rlogin, ProtocolType.RAW, ProtocolType.MSRA)]
    //public virtual SecureString Password
    public virtual string Password
    {
        get => ResolveSecretText(nameof(Password), ref _password, ref _pendingPassword);
        set => SetSecureStringField(ref _password, ref _pendingPassword, value, nameof(Password));
    }

    /// <summary>
    /// The same password as <see cref="Password"/>, for callers that can consume a
    /// <see cref="SecureString"/>. <b>The caller owns what it gets back and must dispose it.</b>
    /// </summary>
    /// <remarks>
    /// A copy, never the stored instance: handing back the record's own secret would let any caller
    /// dispose it, and the next read of <see cref="Password"/> would then throw from a getter nobody
    /// expects to. Hidden from the property grid, which binds to the string property.
    /// </remarks>
    [Browsable(false)]
    public SecureString SecurePassword => ResolveSecret(nameof(Password), ref _password, ref _pendingPassword);

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.VaultOpenbaoMount)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.VaultOpenbaoMountDescription)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public virtual string VaultOpenbaoMount {
        get => GetPropertyValue(nameof(VaultOpenbaoMount), _vaultMount);
        set => SetField(ref _vaultMount, value, nameof(VaultOpenbaoMount));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.VaultOpenbaoRole)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.VaultOpenbaoRoleDescription)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public virtual string VaultOpenbaoRole {
        get => GetPropertyValue(nameof(VaultOpenbaoRole), _vaultRole);
        set => SetField(ref _vaultRole, value, nameof(VaultOpenbaoRole));
    }

    // external credential provider selector
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.VaultOpenbaoSecretEngine)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVaultOpenbaoSecretEngine)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public VaultOpenbaoSecretEngine VaultOpenbaoSecretEngine {
        get => GetPropertyValue(nameof(VaultOpenbaoSecretEngine), _vaultSecretEngine);
        set => SetField(ref _vaultSecretEngine, value, nameof(VaultOpenbaoSecretEngine));
    }


    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Domain)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDomain)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.IntApp, ProtocolType.PowerShell, ProtocolType.WSL, ProtocolType.VMRC)]
    public string Domain
    {
        get => GetPropertyValue(nameof(Domain), ExpandDomainVariables(_domain))?.Trim() ?? string.Empty;
        set => SetField(ref _domain, value?.Trim() ?? string.Empty, nameof(Domain));
    }

    /// <summary>
    /// Expands <c>%name%</c> tokens in a domain template to the connection's <see cref=nameof(Name)/> value.
    /// </summary>
    private string ExpandDomainVariables(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.Contains('%'))
            return raw;
        return raw.Replace("%name%", _name, StringComparison.OrdinalIgnoreCase);
    }


    // external address provider selector
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalAddressProvider)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalAddressProvider)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public ExternalAddressProvider ExternalAddressProvider
    {
        get => GetPropertyValue(nameof(ExternalAddressProvider), _externalAddressProvider);
        set => SetField(ref _externalAddressProvider, value, nameof(ExternalAddressProvider));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.EC2InstanceId)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEC2InstanceId)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public string EC2InstanceId
    {
        get => GetPropertyValue(nameof(EC2InstanceId), _ec2InstanceId)?.Trim() ?? string.Empty;
        set => SetField(ref _ec2InstanceId, value?.Trim() ?? string.Empty, nameof(EC2InstanceId));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.EC2Region)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEC2Region)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.SSH2, ProtocolType.SSHNative)]
    public string EC2Region
    {
        get => GetPropertyValue(nameof(EC2Region), _ec2Region)?.Trim() ?? string.Empty;
        set => SetField(ref _ec2Region, value?.Trim() ?? string.Empty, nameof(EC2Region));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.VmId)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVmId)),
     AttributeUsedInProtocol(ProtocolType.RDP, ProtocolType.VMRC)]
    public string VmId
    {
        get => GetPropertyValue(nameof(VmId), _vmId)?.Trim() ?? string.Empty;
        set => SetField(ref _vmId, value?.Trim() ?? string.Empty, nameof(VmId));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.SshTunnel)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionSshTunnel)),
     TypeConverter(typeof(SshTunnelTypeConverter)),
     AttributeUsedInAllProtocolsExcept()]
    public string SSHTunnelConnectionName
    {
        get => GetPropertyValue(nameof(SSHTunnelConnectionName), _sshTunnelConnectionName)?.Trim() ?? string.Empty;
        set => SetField(ref _sshTunnelConnectionName, value?.Trim() ?? string.Empty, nameof(SSHTunnelConnectionName));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.OpeningCommand)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionOpeningCommand)),
     AttributeUsedInProtocol(ProtocolType.SSH1, ProtocolType.SSH2)]
    public virtual string OpeningCommand
    {
        get => GetPropertyValue(nameof(OpeningCommand), _openingCommand);
        set => SetField(ref _openingCommand, value, nameof(OpeningCommand));
    }
    #endregion

    #region Protocol

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Protocol)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionProtocol)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter))]
    public virtual ProtocolType Protocol
    {
        get => GetPropertyValue(nameof(Protocol), _protocol);
        set => SetField(ref _protocol, value, nameof(Protocol));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpVersion)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRdpVersion)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public virtual RdpVersion RdpVersion
    {
        get => GetPropertyValue(nameof(RdpVersion), _rdpProtocolVersion);
        set => SetField(ref _rdpProtocolVersion, value, nameof(RdpVersion));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalTool)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalTool)),
     TypeConverter(typeof(ExternalToolsTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.IntApp)]
    public string ExtApp
    {
        get => GetPropertyValue(nameof(ExtApp), _extApp);
        set => SetField(ref _extApp, value, nameof(ExtApp));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.PuttySession)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionPuttySession)),
     TypeConverter(typeof(Config.Putty.PuttySessionsManager.SessionList)),
     AttributeUsedInProtocol(ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.Telnet,
         ProtocolType.RAW, ProtocolType.Rlogin)]
    public virtual string PuttySession
    {
        get => GetPropertyValue(nameof(PuttySession), _puttySession);
        set => SetField(ref _puttySession, value, nameof(PuttySession));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.SshOptions)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionSshOptions)),
     AttributeUsedInProtocol(ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.OpenSSH)]
    public virtual string SSHOptions
    {
        get => GetPropertyValue(nameof(SSHOptions), _sshOptions);
        set => SetField(ref _sshOptions, value, nameof(SSHOptions));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("Private Key File"),
     Description("Path to a private key file for SSH authentication. PuTTY-backed protocols expect a PuTTY key (.ppk), passed to PuTTY via the -i argument; the native SSH terminal expects an OpenSSH-format key."),
     Editor(typeof(UI.Controls.ConnectionInfoPropertyGrid.PrivateKeyFileEditor), typeof(System.Drawing.Design.UITypeEditor)),
     AttributeUsedInProtocol(ProtocolType.SSH1, ProtocolType.SSH2, ProtocolType.OpenSSH, ProtocolType.SSHNative)]
    public virtual string PrivateKeyPath
    {
        get => GetPropertyValue(nameof(PrivateKeyPath), _privateKeyPath);
        set => SetField(ref _privateKeyPath, value, nameof(PrivateKeyPath));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseConsoleSession)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseConsoleSession)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseConsoleSession
    {
        get => GetPropertyValue(nameof(UseConsoleSession), _useConsoleSession);
        set => SetField(ref _useConsoleSession, value, nameof(UseConsoleSession));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.AuthenticationLevel)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionAuthenticationLevel)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public AuthenticationLevel RDPAuthenticationLevel
    {
        get => GetPropertyValue(nameof(RDPAuthenticationLevel), _rdpAuthenticationLevel);
        set => SetField(ref _rdpAuthenticationLevel, value, nameof(RDPAuthenticationLevel));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.MinutesToIdleTimeout)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDPMinutesToIdleTimeout)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public virtual int RDPMinutesToIdleTimeout
    {
        get => GetPropertyValue(nameof(RDPMinutesToIdleTimeout), _rdpMinutesToIdleTimeout);
        set
        {
            if (value < 0)
                value = 0;
            else if (value > 240)
                value = 240;
            SetField(ref _rdpMinutesToIdleTimeout, value, nameof(RDPMinutesToIdleTimeout));
        }
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.MinutesToIdleTimeout)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDPAlertIdleTimeout)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RDPAlertIdleTimeout
    {
        get => GetPropertyValue(nameof(RDPAlertIdleTimeout), _rdpAlertIdleTimeout);
        set => SetField(ref _rdpAlertIdleTimeout, value, nameof(RDPAlertIdleTimeout));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.LoadBalanceInfo)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionLoadBalanceInfo)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string LoadBalanceInfo
    {
        get => GetPropertyValue(nameof(LoadBalanceInfo), _loadBalanceInfo)?.Trim() ?? string.Empty;
        set => SetField(ref _loadBalanceInfo, value?.Trim() ?? string.Empty, nameof(LoadBalanceInfo));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("RDP Sign Scope"),
     Description("The signscope value from a signed RDP file. Defines which connection properties are covered by the signature."),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDPSignScope
    {
        get => GetPropertyValue(nameof(RDPSignScope), _rdpSignScope);
        set => SetField(ref _rdpSignScope, value, nameof(RDPSignScope));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("RDP Signature"),
     Description("The signature value from a signed RDP file. Used by RD Connection Broker to validate that connection settings have not been tampered with."),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDPSignature
    {
        get => GetPropertyValue(nameof(RDPSignature), _rdpSignature);
        set => SetField(ref _rdpSignature, value, nameof(RDPSignature));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RenderingEngine)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRenderingEngine)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.HTTP, ProtocolType.HTTPS)]
    public HTTPBase.RenderingEngine RenderingEngine
    {
        get => GetPropertyValue(nameof(RenderingEngine), _renderingEngine);
        set => SetField(ref _renderingEngine, value, nameof(RenderingEngine));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.HttpPath)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionHttpPath)),
     AttributeUsedInProtocol(ProtocolType.HTTP, ProtocolType.HTTPS)]
    public virtual string HttpPath
    {
        get => GetPropertyValue(nameof(HttpPath), _httpPath);
        set => SetField(ref _httpPath, value ?? string.Empty, nameof(HttpPath));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("Suppress Script Errors"),
     Description("If enabled, script errors in the browser will be suppressed."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.HTTP, ProtocolType.HTTPS)]
    public bool ScriptErrorsSuppressed
    {
        get => GetPropertyValue(nameof(ScriptErrorsSuppressed), _scriptErrorsSuppressed);
        set => SetField(ref _scriptErrorsSuppressed, value, nameof(ScriptErrorsSuppressed));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("Use Persistent Browser"),
     Description("If enabled, browser cookies and data will be saved across sessions."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.HTTP, ProtocolType.HTTPS)]
    public bool UsePersistentBrowser
    {
        get => GetPropertyValue(nameof(UsePersistentBrowser), _usePersistentBrowser);
        set => SetField(ref _usePersistentBrowser, value, nameof(UsePersistentBrowser));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     DisplayName("Show Navigation Bar"),
     Description("If enabled, a navigation bar with back/forward/refresh buttons and an address box is shown above the embedded browser."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.HTTP, ProtocolType.HTTPS)]
    public bool ShowBrowserNavigationBar
    {
        get => GetPropertyValue(nameof(ShowBrowserNavigationBar), _showBrowserNavigationBar);
        set => SetField(ref _showBrowserNavigationBar, value, nameof(ShowBrowserNavigationBar));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseCredSsp)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseCredSsp)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseCredSsp
    {
        get => GetPropertyValue(nameof(UseCredSsp), _useCredSsp);
        set => SetField(ref _useCredSsp, value, nameof(UseCredSsp));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseRestrictedAdmin)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseRestrictedAdmin)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseRestrictedAdmin
    {
        get => GetPropertyValue(nameof(UseRestrictedAdmin), _useRestrictedAdmin);
        set => SetField(ref _useRestrictedAdmin, value, nameof(UseRestrictedAdmin));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseRCG)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseRCG)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseRCG
    {
        get => GetPropertyValue(nameof(UseRCG), _useRCG);
        set => SetField(ref _useRCG, value, nameof(UseRCG));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseRedirectionServerName)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseRedirectionServerName)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseRedirectionServerName
    {
        get => GetPropertyValue(nameof(UseRedirectionServerName), _useRedirectionServerName);
        set => SetField(ref _useRedirectionServerName, value, nameof(UseRedirectionServerName));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseVmId)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseVmId)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseVmId
    {
        get => GetPropertyValue(nameof(UseVmId), _useVmId);
        set => SetField(ref _useVmId, value, nameof(UseVmId));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Protocol), 3),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UseEnhancedMode)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUseEnhancedMode)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool UseEnhancedMode
    {
        get => GetPropertyValue(nameof(UseEnhancedMode), _useEnhancedMode);
        set => SetField(ref _useEnhancedMode, value, nameof(UseEnhancedMode));
    }
    #endregion

    #region RD Gateway

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayUsageMethod)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRdpGatewayUsageMethod)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDGatewayUsageMethod RDGatewayUsageMethod
    {
        get => GetPropertyValue(nameof(RDGatewayUsageMethod), _rdGatewayUsageMethod);
        set => SetField(ref _rdGatewayUsageMethod, value, nameof(RDGatewayUsageMethod));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayHostname)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDGatewayHostname)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDGatewayHostname
    {
        get => GetPropertyValue(nameof(RDGatewayHostname), _rdGatewayHostname)?.Trim() ?? string.Empty;
        set => SetField(ref _rdGatewayHostname, value?.Trim() ?? string.Empty, nameof(RDGatewayHostname));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayUseConnectionCredentials)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDGatewayUseConnectionCredentials)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDGatewayUseConnectionCredentials RDGatewayUseConnectionCredentials
    {
        get => GetPropertyValue(nameof(RDGatewayUseConnectionCredentials), _rdGatewayUseConnectionCredentials);
        set => SetField(ref _rdGatewayUseConnectionCredentials, value, nameof(RDGatewayUseConnectionCredentials));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayUsername)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDGatewayUsername)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDGatewayUsername
    {
        get => GetPropertyValue(nameof(RDGatewayUsername), _rdGatewayUsername)?.Trim() ?? string.Empty;
        set => SetField(ref _rdGatewayUsername, value?.Trim() ?? string.Empty, nameof(RDGatewayUsername));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayPassword)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRdpGatewayPassword)),
     PasswordPropertyText(true),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDGatewayPassword
    {
        get => ResolveSecretText(nameof(RDGatewayPassword), ref _rdGatewayPassword, ref _pendingRdGatewayPassword);
        set => SetSecureStringField(ref _rdGatewayPassword, ref _pendingRdGatewayPassword, value, nameof(RDGatewayPassword));
    }

    /// <summary>
    /// The same secret as <see cref="RDGatewayPassword"/>, as a <see cref="SecureString"/> the
    /// caller owns and must dispose. See <see cref="SecurePassword"/>.
    /// </summary>
    [Browsable(false)]
    public SecureString SecureRDGatewayPassword =>
        ResolveSecret(nameof(RDGatewayPassword), ref _rdGatewayPassword, ref _pendingRdGatewayPassword);

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayAccessToken)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRdpGatewayAccessToken)),
     PasswordPropertyText(true),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDGatewayAccessToken
    {
        get => GetPropertyValue(nameof(RDGatewayAccessToken), _rdGatewayAccessToken);
        set => SetField(ref _rdGatewayAccessToken, value, nameof(RDGatewayAccessToken));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RdpGatewayDomain)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDGatewayDomain)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RDGatewayDomain
    {
        get => GetPropertyValue(nameof(RDGatewayDomain), _rdGatewayDomain)?.Trim() ?? string.Empty;
        set => SetField(ref _rdGatewayDomain, value?.Trim() ?? string.Empty, nameof(RDGatewayDomain));
    }
    // external credential provider selector for rd gateway
    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalCredentialProvider)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalCredentialProvider)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public ExternalCredentialProvider RDGatewayExternalCredentialProvider
    {
        get => GetPropertyValue(nameof(RDGatewayExternalCredentialProvider), _rdGatewayExternalCredentialProvider);
        set => SetField(ref _rdGatewayExternalCredentialProvider, value, nameof(RDGatewayExternalCredentialProvider));
    }

    // credential record identifier for external credential provider
    [LocalizedAttributes.LocalizedCategory(nameof(Language.RDPGateway), 4),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UserViaAPI)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUserViaAPI)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public virtual string RDGatewayUserViaAPI
    {
        get => GetPropertyValue(nameof(RDGatewayUserViaAPI), _rdGatewayUserViaAPI);
        set => SetField(ref _rdGatewayUserViaAPI, value, nameof(RDGatewayUserViaAPI));
    }
    #endregion

    #region Appearance

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Resolution)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionResolution)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPResolutions Resolution
    {
        get => GetPropertyValue(nameof(Resolution), _resolution);
        set => SetField(ref _resolution, value, nameof(Resolution));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     DisplayName("Sizing Mode"),
     Description("Controls how the remote desktop is scaled to fit the panel. SmartSize stretches to fill; SmartSize (Aspect Ratio) preserves aspect ratio."),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPSizingMode RDPSizingMode
    {
        get => GetPropertyValue(nameof(RDPSizingMode), _rdpSizingMode);
        set => SetField(ref _rdpSizingMode, value, nameof(RDPSizingMode));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     DisplayName("Resolution Width"),
     Description("Custom resolution width in pixels (used when Resolution is set to Custom)."),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public int ResolutionWidth
    {
        get => GetPropertyValue(nameof(ResolutionWidth), _resolutionWidth);
        set => SetField(ref _resolutionWidth, value, nameof(ResolutionWidth));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     DisplayName("Resolution Height"),
     Description("Custom resolution height in pixels (used when Resolution is set to Custom)."),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public int ResolutionHeight
    {
        get => GetPropertyValue(nameof(ResolutionHeight), _resolutionHeight);
        set => SetField(ref _resolutionHeight, value, nameof(ResolutionHeight));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     DisplayName("Zoom Level (Desktop Scale Factor)"),
     Description("Controls RDP zoom for this connection. 'Auto' follows the local display scale; fixed values force a specific zoom level."),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPDesktopScaleFactor DesktopScaleFactor
    {
        get => GetPropertyValue(nameof(DesktopScaleFactor), _desktopScaleFactor);
        set => SetField(ref _desktopScaleFactor, value, nameof(DesktopScaleFactor));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.AutomaticResize)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionAutomaticResize)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool AutomaticResize
    {
        get => GetPropertyValue(nameof(AutomaticResize), _automaticResize);
        set => SetField(ref _automaticResize, value, nameof(AutomaticResize));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     DisplayName("Use Multiple Monitors"),
     Description("When enabled and connecting in fullscreen, the RDP session spans all local monitors. Requires RDP 8.1 or later."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RDPUseMultimon
    {
        get => GetPropertyValue(nameof(RDPUseMultimon), _rdpUseMultimon);
        set => SetField(ref _rdpUseMultimon, value, nameof(RDPUseMultimon));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Colors)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionColors)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPColors Colors
    {
        get => GetPropertyValue(nameof(Colors), _colors);
        set => SetField(ref _colors, value, nameof(Colors));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.CacheBitmaps)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionCacheBitmaps)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool CacheBitmaps
    {
        get => GetPropertyValue(nameof(CacheBitmaps), _cacheBitmaps);
        set => SetField(ref _cacheBitmaps, value, nameof(CacheBitmaps));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisplayWallpaper)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisplayWallpaper)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisplayWallpaper
    {
        get => GetPropertyValue(nameof(DisplayWallpaper), _displayWallpaper);
        set => SetField(ref _displayWallpaper, value, nameof(DisplayWallpaper));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisplayThemes)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisplayThemes)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisplayThemes
    {
        get => GetPropertyValue(nameof(DisplayThemes), _displayThemes);
        set => SetField(ref _displayThemes, value, nameof(DisplayThemes));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.FontSmoothing)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEnableFontSmoothing)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool EnableFontSmoothing
    {
        get => GetPropertyValue(nameof(EnableFontSmoothing), _enableFontSmoothing);
        set => SetField(ref _enableFontSmoothing, value, nameof(EnableFontSmoothing));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.EnableDesktopComposition)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEnableDesktopComposition)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool EnableDesktopComposition
    {
        get => GetPropertyValue(nameof(EnableDesktopComposition), _enableDesktopComposition);
        set => SetField(ref _enableDesktopComposition, value, nameof(EnableDesktopComposition));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisableFullWindowDrag)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisableFullWindowDrag)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisableFullWindowDrag
    {
        get => GetPropertyValue(nameof(DisableFullWindowDrag), _disableFullWindowDrag);
        set => SetField(ref _disableFullWindowDrag, value, nameof(DisableFullWindowDrag));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisableMenuAnimations)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisableMenuAnimations)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisableMenuAnimations
    {
        get => GetPropertyValue(nameof(DisableMenuAnimations), _disableMenuAnimations);
        set => SetField(ref _disableMenuAnimations, value, nameof(DisableMenuAnimations));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisableCursorShadow)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisableCursorShadow)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisableCursorShadow
    {
        get => GetPropertyValue(nameof(DisableCursorShadow), _disableCursorShadow);
        set => SetField(ref _disableCursorShadow, value, nameof(DisableCursorShadow));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DisableCursorShadow)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionDisableCursorShadow)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool DisableCursorBlinking
    {
        get => GetPropertyValue(nameof(DisableCursorBlinking), _disableCursorBlinking);
        set => SetField(ref _disableCursorBlinking, value, nameof(DisableCursorBlinking));
    }
    #endregion

    #region Redirect

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RedirectKeys)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectKeys)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectKeys
    {
        get => GetPropertyValue(nameof(RedirectKeys), _redirectKeys);
        set => SetField(ref _redirectKeys, value, nameof(RedirectKeys));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.DiskDrives)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectDrives)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPDiskDrives RedirectDiskDrives
    {
        get => GetPropertyValue(nameof(RedirectDiskDrives), _redirectDiskDrives);
        set => SetField(ref _redirectDiskDrives, value, nameof(RedirectDiskDrives));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RedirectDiskDrivesCustom)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectDiskDrivesCustom)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public string RedirectDiskDrivesCustom
    {
        get => GetPropertyValue(nameof(RedirectDiskDrivesCustom), _redirectDiskDrivesCustom);
        set => SetField(ref _redirectDiskDrivesCustom, value, nameof(RedirectDiskDrivesCustom));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Printers)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectPrinters)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectPrinters
    {
        get => GetPropertyValue(nameof(RedirectPrinters), _redirectPrinters);
        set => SetField(ref _redirectPrinters, value, nameof(RedirectPrinters));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Clipboard)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectClipboard)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectClipboard
    {
        get => GetPropertyValue(nameof(RedirectClipboard), _redirectClipboard);
        set => SetField(ref _redirectClipboard, value, nameof(RedirectClipboard));
    }


    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Ports)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectPorts)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectPorts
    {
        get => GetPropertyValue(nameof(RedirectPorts), _redirectPorts);
        set => SetField(ref _redirectPorts, value, nameof(RedirectPorts));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.SmartCard)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectSmartCards)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectSmartCards
    {
        get => GetPropertyValue(nameof(RedirectSmartCards), _redirectSmartCards);
        set => SetField(ref _redirectSmartCards, value, nameof(RedirectSmartCards));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Sounds)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectSounds)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPSounds RedirectSound
    {
        get => GetPropertyValue(nameof(RedirectSound), _redirectSound);
        set => SetField(ref _redirectSound, value, nameof(RedirectSound));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.SoundQuality)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionSoundQuality)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public RDPSoundQuality SoundQuality
    {
        get => GetPropertyValue(nameof(SoundQuality), _soundQuality);
        set => SetField(ref _soundQuality, value, nameof(SoundQuality));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.AudioCapture)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectAudioCapture)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectAudioCapture
    {
        get => GetPropertyValue(nameof(RedirectAudioCapture), _redirectAudioCapture);
        set => SetField(ref _redirectAudioCapture, value, nameof(RedirectAudioCapture));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.WebAuthn)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRedirectWebAuthn)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool RedirectWebAuthn
    {
        get => GetPropertyValue(nameof(RedirectWebAuthn), _redirectWebAuthn);
        set => SetField(ref _redirectWebAuthn, value, nameof(RedirectWebAuthn));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.EntraIdAuthentication)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEnableRdsAadAuth)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public bool EnableRdsAadAuth
    {
        get => GetPropertyValue(nameof(EnableRdsAadAuth), _enableRdsAadAuth);
        set => SetField(ref _enableRdsAadAuth, value, nameof(EnableRdsAadAuth));
    }

    #endregion

    #region Misc

    [Browsable(false)] public string ConstantID { get; } = uniqueId.ThrowIfNullOrEmpty(nameof(uniqueId));

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalToolBefore)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalToolBefore)),
     TypeConverter(typeof(ExternalToolsTypeConverter))]
    public virtual string PreExtApp
    {
        get => GetPropertyValue(nameof(PreExtApp), _preExtApp);
        set => SetField(ref _preExtApp, value, nameof(PreExtApp));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ExternalToolAfter)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionExternalToolAfter)),
     TypeConverter(typeof(ExternalToolsTypeConverter))]
    public virtual string PostExtApp
    {
        get => GetPropertyValue(nameof(PostExtApp), _postExtApp);
        set => SetField(ref _postExtApp, value, nameof(PostExtApp));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.MacAddress)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionMACAddress))]
    public virtual string MacAddress
    {
        get => GetPropertyValue(nameof(MacAddress), _macAddress);
        set => SetField(ref _macAddress, value, nameof(MacAddress));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.UserField)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionUser1))]
    public virtual string UserField
    {
        get => GetPropertyValue(nameof(UserField), _userField);
        set => SetField(ref _userField, value, nameof(UserField));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 1"),
     Description("Additional user-defined field 1 for custom data. Available as %USERFIELD1% token in external tools.")]
    public virtual string UserField1
    {
        get => GetPropertyValue(nameof(UserField1), _userField1);
        set => SetField(ref _userField1, value, nameof(UserField1));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 2"),
     Description("Additional user-defined field 2 for custom data. Available as %USERFIELD2% token in external tools.")]
    public virtual string UserField2
    {
        get => GetPropertyValue(nameof(UserField2), _userField2);
        set => SetField(ref _userField2, value, nameof(UserField2));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 3"),
     Description("Additional user-defined field 3 for custom data. Available as %USERFIELD3% token in external tools.")]
    public virtual string UserField3
    {
        get => GetPropertyValue(nameof(UserField3), _userField3);
        set => SetField(ref _userField3, value, nameof(UserField3));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 4"),
     Description("Additional user-defined field 4 for custom data. Available as %USERFIELD4% token in external tools.")]
    public virtual string UserField4
    {
        get => GetPropertyValue(nameof(UserField4), _userField4);
        set => SetField(ref _userField4, value, nameof(UserField4));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 5"),
     Description("Additional user-defined field 5 for custom data. Available as %USERFIELD5% token in external tools.")]
    public virtual string UserField5
    {
        get => GetPropertyValue(nameof(UserField5), _userField5);
        set => SetField(ref _userField5, value, nameof(UserField5));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 6"),
     Description("Additional user-defined field 6 for custom data. Available as %USERFIELD6% token in external tools.")]
    public virtual string UserField6
    {
        get => GetPropertyValue(nameof(UserField6), _userField6);
        set => SetField(ref _userField6, value, nameof(UserField6));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 7"),
     Description("Additional user-defined field 7 for custom data. Available as %USERFIELD7% token in external tools.")]
    public virtual string UserField7
    {
        get => GetPropertyValue(nameof(UserField7), _userField7);
        set => SetField(ref _userField7, value, nameof(UserField7));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 8"),
     Description("Additional user-defined field 8 for custom data. Available as %USERFIELD8% token in external tools.")]
    public virtual string UserField8
    {
        get => GetPropertyValue(nameof(UserField8), _userField8);
        set => SetField(ref _userField8, value, nameof(UserField8));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 9"),
     Description("Additional user-defined field 9 for custom data. Available as %USERFIELD9% token in external tools.")]
    public virtual string UserField9
    {
        get => GetPropertyValue(nameof(UserField9), _userField9);
        set => SetField(ref _userField9, value, nameof(UserField9));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("User Field 10"),
     Description("Additional user-defined field 10 for custom data. Available as %USERFIELD10% token in external tools.")]
    public virtual string UserField10
    {
        get => GetPropertyValue(nameof(UserField10), _userField10);
        set => SetField(ref _userField10, value, nameof(UserField10));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName(nameof(Notes)),
     Description("Free-form multiline notes for this connection.")]
    public virtual string Notes
    {
        get => GetPropertyValue(nameof(Notes), _notes);
        set => SetField(ref _notes, value, nameof(Notes));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.EnvironmentTags)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEnvironmentTags))]
    public virtual string EnvironmentTags
    {
        get => GetPropertyValue(nameof(EnvironmentTags), _environmentTags);
        set => SetField(ref _environmentTags, value, nameof(EnvironmentTags));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Favorite)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionFavorite)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter))]
    public virtual bool Favorite
    {
        get => GetPropertyValue(nameof(Favorite), _favorite);
        set => SetField(ref _favorite, value, nameof(Favorite));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("Retry On First Connect"),
     Description("If enabled, the reconnect dialog will be shown when the initial connection attempt fails, polling the server until it becomes available."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInAllProtocolsExcept()]
    public bool RetryOnFirstConnect
    {
        get => GetPropertyValue(nameof(RetryOnFirstConnect), _retryOnFirstConnect);
        set => SetField(ref _retryOnFirstConnect, value, nameof(RetryOnFirstConnect));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("Wait For IP Availability"),
     Description("If enabled, mRemoteNG will poll the host:port before connecting, waiting until it becomes reachable."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInAllProtocolsExcept()]
    public bool WaitForIPAvailability
    {
        get => GetPropertyValue(nameof(WaitForIPAvailability), _waitForIPAvailability);
        set => SetField(ref _waitForIPAvailability, value, nameof(WaitForIPAvailability));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     DisplayName("Wait For IP Timeout"),
     Description("Timeout in seconds when waiting for a host to become reachable (default: 60)."),
     AttributeUsedInAllProtocolsExcept()]
    public int WaitForIPTimeout
    {
        get => GetPropertyValue(nameof(WaitForIPTimeout), _waitForIPTimeout);
        set => SetField(ref _waitForIPTimeout, value, nameof(WaitForIPTimeout));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     DisplayName("Always Prompt For Credentials"),
     Description("If enabled, a credential dialog will be shown every time this connection is opened, instead of using stored credentials."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInAllProtocolsExcept(ProtocolType.Telnet, ProtocolType.Rlogin, ProtocolType.RAW, ProtocolType.MSRA)]
    public bool AlwaysPromptForCredentials
    {
        get => GetPropertyValue(nameof(AlwaysPromptForCredentials), _alwaysPromptForCredentials);
        set => SetField(ref _alwaysPromptForCredentials, value, nameof(AlwaysPromptForCredentials));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RDPStartProgram)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDPStartProgram)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public virtual string RDPStartProgram
    {
        get => GetPropertyValue(nameof(RDPStartProgram), _rdpStartProgram);
        set => SetField(ref _rdpStartProgram, value, nameof(RDPStartProgram));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.RDPStartProgramWorkDir)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionRDPStartProgramWorkDir)),
     AttributeUsedInProtocol(ProtocolType.RDP)]
    public virtual string RDPStartProgramWorkDir
    {
        get => GetPropertyValue(nameof(RDPStartProgramWorkDir), _rdpStartProgramWorkDir);
        set => SetField(ref _rdpStartProgramWorkDir, value, nameof(RDPStartProgramWorkDir));
    }

    #endregion

    #region VNC
    // TODO: it seems all these VNC properties were added and serialized but
    // never hooked up to the VNC protocol or shown to the user
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Compression)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionCompression)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public ProtocolVNC.Compression VNCCompression
    {
        get => GetPropertyValue(nameof(VNCCompression), _vncCompression);
        set => SetField(ref _vncCompression, value, nameof(VNCCompression));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Encoding)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionEncoding)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public ProtocolVNC.Encoding VNCEncoding
    {
        get => GetPropertyValue(nameof(VNCEncoding), _vncEncoding);
        set => SetField(ref _vncEncoding, value, nameof(VNCEncoding));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Connection), 2),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.AuthenticationMode)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionAuthenticationMode)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public ProtocolVNC.AuthMode VNCAuthMode
    {
        get => GetPropertyValue(nameof(VNCAuthMode), _vncAuthMode);
        set => SetField(ref _vncAuthMode, value, nameof(VNCAuthMode));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Proxy), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ProxyType)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVNCProxyType)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public ProtocolVNC.ProxyType VNCProxyType
    {
        get => GetPropertyValue(nameof(VNCProxyType), _vncProxyType);
        set => SetField(ref _vncProxyType, value, nameof(VNCProxyType));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Proxy), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ProxyAddress)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVNCProxyAddress)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public string VNCProxyIP
    {
        get => GetPropertyValue(nameof(VNCProxyIP), _vncProxyIp);
        set => SetField(ref _vncProxyIp, value, nameof(VNCProxyIP));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Proxy), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ProxyPort)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVNCProxyPort)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public int VNCProxyPort
    {
        get => GetPropertyValue(nameof(VNCProxyPort), _vncProxyPort);
        set => SetField(ref _vncProxyPort, value, nameof(VNCProxyPort));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Proxy), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ProxyUsername)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVNCProxyUsername)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public string VNCProxyUsername
    {
        get => GetPropertyValue(nameof(VNCProxyUsername), _vncProxyUsername);
        set => SetField(ref _vncProxyUsername, value, nameof(VNCProxyUsername));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Proxy), 7),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ProxyPassword)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionVNCProxyPassword)),
     PasswordPropertyText(true),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD),
     Browsable(false)]
    public string VNCProxyPassword
    {
        get => ResolveSecretText(nameof(VNCProxyPassword), ref _vncProxyPassword, ref _pendingVncProxyPassword);
        set => SetSecureStringField(ref _vncProxyPassword, ref _pendingVncProxyPassword, value, nameof(VNCProxyPassword));
    }

    /// <summary>
    /// The same secret as <see cref="VNCProxyPassword"/>, as a <see cref="SecureString"/> the caller
    /// owns and must dispose. See <see cref="SecurePassword"/>.
    /// </summary>
    [Browsable(false)]
    public SecureString SecureVNCProxyPassword =>
        ResolveSecret(nameof(VNCProxyPassword), ref _vncProxyPassword, ref _pendingVncProxyPassword);

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Colors)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionColors)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD)]
    public ProtocolVNC.Colors VNCColors
    {
        get => GetPropertyValue(nameof(VNCColors), _vncColors);
        set => SetField(ref _vncColors, value, nameof(VNCColors));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.SmartSizeMode)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionSmartSizeMode)),
     TypeConverter(typeof(MiscTools.EnumTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD)]
    public ProtocolVNC.SmartSizeMode VNCSmartSizeMode
    {
        get => GetPropertyValue(nameof(VNCSmartSizeMode), _vncSmartSizeMode);
        set => SetField(ref _vncSmartSizeMode, value, nameof(VNCSmartSizeMode));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Appearance), 5),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.ViewOnly)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionViewOnly)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD)]
    public bool VNCViewOnly
    {
        get => GetPropertyValue(nameof(VNCViewOnly), _vncViewOnly);
        set => SetField(ref _vncViewOnly, value, nameof(VNCViewOnly));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Redirect), 6),
     Browsable(true),
     DisplayName("VNC Clipboard Redirect"),
     Description("If enabled, the local clipboard is shared with the remote VNC server."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter)),
     AttributeUsedInProtocol(ProtocolType.VNC, ProtocolType.ARD)]
    public bool VNCClipboardRedirect
    {
        get => GetPropertyValue(nameof(VNCClipboardRedirect), _vncClipboardRedirect);
        set => SetField(ref _vncClipboardRedirect, value, nameof(VNCClipboardRedirect));
    }

    #endregion
    #endregion

    protected virtual TPropertyType GetPropertyValue<TPropertyType>(string propertyName, TPropertyType value)
    {
        var result = GetType().GetProperty(propertyName)?.GetValue(this, null);
        return result is TPropertyType typed ? typed : value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void RaisePropertyChangedEvent(object sender, PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(args.PropertyName));
    }

    protected void SetField<T>(ref T field, T value, string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        RaisePropertyChangedEvent(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string ConvertToUnsecureStringOrEmpty(SecureString? password)
    {
        return password?.ConvertToUnsecureString() ?? string.Empty;
    }

    /// <summary>
    /// A value no resolution can produce, used to tell "nothing overrode this record" apart from
    /// "something resolved to an empty secret". Reference identity is the signal, so it must be a
    /// distinct instance rather than a literal the runtime may intern.
    /// </summary>
    private static readonly string Unresolved = new([.. " unresolved"]);

    /// <summary>
    /// Resolves a secret the same way its plain-text property does, avoiding the plain text wherever
    /// the answer is already a <see cref="SecureString"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the three routes never need a string. This record's own value is one, and a bound
    /// credential record is the other — and the second is the one that matters, because the property
    /// grid re-reads a displayed connection constantly and each read was converting the credential's
    /// <see cref="SecureString"/> into a string that cannot be zeroed.
    /// </para>
    /// <para>
    /// <b>The remaining routes deliberately go through the plain-text property.</b> Inheritance and
    /// connection links resolve by reading <i>another record's</i> string property — including a walk
    /// up the tree that skips parents holding an empty credential — and reimplementing those rules
    /// here would be a second copy of them, free to drift from the first. A copy that drifts would
    /// hand a caller the wrong password, which is far worse than the copy this saves. The string in
    /// those cases exists whether this asks for it or not.
    /// </para>
    /// </remarks>
    private protected SecureString ResolveSecret(string propertyName, ref SecureString? own,
                                                 ref PendingConnectionSecret? pending)
    {
        if (TryGetSecretWithoutPlainText(propertyName, out SecureString? direct))
            return direct!;

        string resolved = GetPropertyValue(propertyName, Unresolved);
        if (!ReferenceEquals(resolved, Unresolved))
            return resolved.ConvertToSecureString();

        return OwnSecretCopy(propertyName, ref own, ref pending);
    }

    /// <summary>
    /// Resolves a secret as its plain-text property does, decrypting this record's own stored value
    /// only if the answer turns out to come from here.
    /// </summary>
    /// <remarks>
    /// The order is what makes deferring worth anything. A connection bound to a credential record,
    /// linked to another connection, or inheriting from its folder answers from somewhere else, and
    /// deciding that first means its own stored secret is never decrypted at all.
    /// </remarks>
    private string ResolveSecretText(string propertyName, ref SecureString? own,
                                     ref PendingConnectionSecret? pending)
    {
        string resolved = GetPropertyValue(propertyName, Unresolved);
        if (!ReferenceEquals(resolved, Unresolved))
            return resolved;

        return OwnSecretText(propertyName, ref own, ref pending);
    }

    /// <summary>
    /// A copy of this record's own secret, decrypting the ciphertext it was loaded with if that has
    /// not happened yet.
    /// </summary>
    /// <remarks>
    /// <b>The copy is made while the lock is held</b>, and that is the whole reason this returns a
    /// value rather than the stored instance. A setter arriving between the resolution and the copy
    /// disposes what was resolved, and the caller would meet an
    /// <see cref="ObjectDisposedException"/> from a getter nobody expects to throw.
    /// </remarks>
    private SecureString OwnSecretCopy(string secretName, ref SecureString? own,
                                       ref PendingConnectionSecret? pending)
    {
        lock (SecretLock)
        {
            ResolveOwnSecret(secretName, ref own, ref pending);
            return own?.Copy() ?? new SecureString();
        }
    }

    /// <summary>
    /// This record's own secret as plain text, decrypting first if it has not been decrypted yet.
    /// </summary>
    /// <remarks>
    /// Converted under the lock for the same reason <see cref="OwnSecretCopy"/> copies under it.
    /// </remarks>
    private string OwnSecretText(string secretName, ref SecureString? own,
                                 ref PendingConnectionSecret? pending)
    {
        lock (SecretLock)
        {
            ResolveOwnSecret(secretName, ref own, ref pending);
            return own?.ConvertToUnsecureString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Turns this record's pending ciphertext into its stored <see cref="SecureString"/>, the first
    /// time somebody asks for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The caller holds <see cref="SecretLock"/>.</b> Decrypting once is the point: the tree is
    /// read from the user interface thread and from the host-status monitor, so two threads can
    /// arrive together, and without the lock they would each derive a key and build a
    /// <see cref="SecureString"/> of which one would be dropped on the floor undisposed.
    /// </para>
    /// <para>
    /// <b>A failure leaves the pending value where it is</b> and lets the exception out. Clearing it
    /// would make the next read answer with an empty secret, and several callers read empty as "no
    /// password configured" and fall through to the configured default - so the connection would be
    /// attempted with the wrong credentials rather than reported as broken.
    /// </para>
    /// </remarks>
    private void ResolveOwnSecret(string secretName, ref SecureString? own,
                                  ref PendingConnectionSecret? pending)
    {
        if (pending is null)
            return;

        string plainText = pending.Resolve(Name, secretName);
        own?.Dispose();
        own = plainText.ConvertToSecureString();
        pending = null;
    }

    /// <summary>
    /// Hands the record a secret it has not decrypted yet.
    /// </summary>
    /// <remarks>
    /// Called by the deserializer in place of assigning a decrypted value, which is what used to put
    /// every password in a connection file into the process to serve the handful a user opens.
    /// </remarks>
    public virtual void SetPendingSecret(string secretName, PendingConnectionSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        lock (SecretLock)
        {
            switch (secretName)
            {
                case nameof(Password):
                    _password?.Dispose();
                    _password = null;
                    _pendingPassword = secret;
                    break;
                case nameof(RDGatewayPassword):
                    _rdGatewayPassword?.Dispose();
                    _rdGatewayPassword = null;
                    _pendingRdGatewayPassword = secret;
                    break;
                case nameof(VNCProxyPassword):
                    _vncProxyPassword?.Dispose();
                    _vncProxyPassword = null;
                    _pendingVncProxyPassword = secret;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(secretName), secretName,
                        "This record holds no secret by that name.");
            }
        }
    }

    /// <summary>
    /// The stored bytes of a secret nothing has read or replaced, when writing those same bytes back
    /// is still correct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions, and both are load-bearing. The key has to be the one the bytes were read
    /// under - see <see cref="ConnectionSecretKeyIdentity"/> for what writing them back under a
    /// different one costs. And what the property would answer has to be what gets written: a
    /// connection resolving its password from a credential record, from a linked connection or from
    /// its folder must not have this record's own untouched bytes written in place of it.
    /// </para>
    /// <para>
    /// <b>Passing ciphertext through does not regenerate the nonce, and that is correct.</b> Under an
    /// authenticated cipher this writes the same message, under the same key, with the same nonce -
    /// which is what it already was on disk. Nonce reuse is dangerous when two <i>different</i>
    /// plaintexts share one; here the plaintext did not change either, because nothing decrypted it,
    /// let alone edited it. A record whose secret was assigned has no pending value left and is
    /// re-encrypted normally, nonce and all.
    /// </para>
    /// </remarks>
    public virtual bool TryGetStoredCipherText(string secretName, ConnectionSecretKeyIdentity? key, out string cipherText)
    {
        cipherText = string.Empty;

        PendingConnectionSecret? pending;
        lock (SecretLock)
            pending = PendingSecret(secretName);

        if (pending is null || !pending.WasWrittenUnder(key))
            return false;

        // Both of these resolve through the tree and the credential catalogue, so neither may run
        // under the lock a getter also takes.
        if (TryGetSecretWithoutPlainText(secretName, out SecureString? elsewhere))
        {
            elsewhere?.Dispose();
            return false;
        }

        if (!ReferenceEquals(GetPropertyValue(secretName, Unresolved), Unresolved))
            return false;

        lock (SecretLock)
        {
            // Asked again, because the checks above ran without the lock. A secret assigned in that
            // window has already discarded this ciphertext, and writing it anyway would put the
            // superseded password back in the file in place of the one the user just typed.
            if (!ReferenceEquals(PendingSecret(secretName), pending))
                return false;

            cipherText = pending.CipherText;
            return true;
        }
    }

    /// <summary>
    /// The ciphertext slot for one secret, or <see langword="null"/> for a name this record does not
    /// hold. <b>The caller holds <see cref="SecretLock"/>.</b>
    /// </summary>
    private PendingConnectionSecret? PendingSecret(string secretName) =>
        secretName switch
        {
            nameof(Password) => _pendingPassword,
            nameof(RDGatewayPassword) => _pendingRdGatewayPassword,
            nameof(VNCProxyPassword) => _pendingVncProxyPassword,
            _ => null
        };

    /// <summary>
    /// Decrypts every secret this record still holds as ciphertext.
    /// </summary>
    /// <remarks>
    /// For the one case where deferring is not an option: re-encrypting a store under a new key. A
    /// record still holding ciphertext from the old key, written through untouched, produces a file
    /// encrypted under two keys - and the half under the old key can never be read again. Any
    /// failure comes out as an exception, which is what refuses the save.
    /// </remarks>
    /// <exception cref="ConnectionSecretDecryptionException">A stored secret did not decrypt.</exception>
    public virtual void DecryptEveryStoredSecret()
    {
        lock (SecretLock)
        {
            ResolveOwnSecret(nameof(Password), ref _password, ref _pendingPassword);
            ResolveOwnSecret(nameof(RDGatewayPassword), ref _rdGatewayPassword, ref _pendingRdGatewayPassword);
            ResolveOwnSecret(nameof(VNCProxyPassword), ref _vncProxyPassword, ref _pendingVncProxyPassword);
        }
    }

    /// <summary>
    /// Whether this record has a password, answered without decrypting one wherever that is possible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of every node in a file being imported, purely to learn whether there is a credential
    /// worth extracting. A record with nothing stored and nowhere to inherit from answers
    /// immediately, which is the case this exists for.
    /// </para>
    /// <para>
    /// <b>The presence of ciphertext is not on its own an answer.</b> An empty password is written as
    /// the encryption of an empty string rather than as an empty attribute - deliberately, so that
    /// the file does not disclose which connections have no password - so a record holding ciphertext
    /// may still hold nothing. That one has to be decrypted to be sure, and so does a record whose
    /// password resolves from a credential record, a linked connection or its folder.
    /// </para>
    /// </remarks>
    [Browsable(false)]
    public bool HasPassword
    {
        get
        {
            bool nothingStored;
            lock (SecretLock)
                nothingStored = _pendingPassword is null && _password is null;

            if (nothingStored && !SecretsCanResolveElsewhere)
                return false;

            using SecureString resolved = SecurePassword;
            return resolved.Length > 0;
        }
    }

    /// <summary>
    /// Whether a secret could be answered by something other than this record's own stored value.
    /// </summary>
    /// <remarks>
    /// Only ever used to skip work: false has to mean "there is definitely nowhere else", so a
    /// record that gains a new way of borrowing a secret has to be added here.
    /// </remarks>
    private protected virtual bool SecretsCanResolveElsewhere => !string.IsNullOrEmpty(CredentialId);

    /// <summary>
    /// A source that already holds this secret as a <see cref="SecureString"/>, if one applies.
    /// </summary>
    /// <remarks>
    /// Overridden where a connection can be bound to a credential record. The base record has no
    /// such binding, so it answers for itself.
    /// </remarks>
    private protected virtual bool TryGetSecretWithoutPlainText(string propertyName, out SecureString? secret)
    {
        secret = null;
        return false;
    }

    /// <summary>
    /// Serialises resolution of this record's secrets against each other.
    /// </summary>
    /// <remarks>
    /// One lock for all three rather than one each: the only contention that exists is two threads
    /// reading the same record at once, and past the first read it is uncontended anyway.
    /// </remarks>
    private protected readonly Lock SecretLock = new();

    private void SetSecureStringField(ref SecureString? field, ref PendingConnectionSecret? pending,
                                      string value, string? propertyName = null)
    {
        value ??= string.Empty;
        bool changed;

        lock (SecretLock)
        {
            // Discarded whether or not the value below turns out to be different. A record edited
            // before its first read still holds the ciphertext it was loaded with, and leaving that
            // in place would let the next read decrypt over the top of what the user just typed.
            pending = null;

            changed = !string.Equals(ConvertToUnsecureStringOrEmpty(field), value, StringComparison.Ordinal);
            if (changed)
            {
                field?.Dispose();
                field = value.ConvertToSecureString();
            }
        }

        // Outside the lock: a change notification runs whatever the tree, the property grid and the
        // auto-saver have subscribed, and none of that belongs inside a lock a getter also takes.
        if (changed)
            RaisePropertyChangedEvent(this, new PropertyChangedEventArgs(propertyName));
    }
}