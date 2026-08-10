using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Container;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;

namespace mRemoteNG.Tree.Root;

[SupportedOSPlatform("windows")]
[DefaultProperty("Name")]
public class RootNodeInfo(RootNodeType rootType, string uniqueId) : ContainerInfo(uniqueId)
{
    private string _name = Language.Connections;
    private string _customPassword = "";
    private bool _passwordProtect;
    private bool _autoLockOnMinimize;
    private bool _totpEnabled;
    private string _totpSecret = "";

    public RootNodeInfo(RootNodeType rootType)
        : this(rootType, Guid.NewGuid().ToString())
    {
        // Re-set name after base ContainerInfo constructor overrides it via SetDefaults()
        _name = Language.Connections;
    }

    #region Public Properties

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous)),
     Browsable(true),
     LocalizedAttributes.LocalizedDefaultValue(nameof(Language.Connections)),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.Name)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionName))]
    public override string Name
    {
        get => _name;
        set => SetField(ref _name, value, nameof(Name));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous)),
     Browsable(true),
     LocalizedAttributes.LocalizedDisplayName(nameof(Language.PasswordProtect)),
     LocalizedAttributes.LocalizedDescription(nameof(Language.PropertyDescriptionPasswordProtect)),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter))]
    public new bool Password
    {
        get => _passwordProtect;
        set => SetField(ref _passwordProtect, value, nameof(Password));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous)),
     Browsable(true),
     DisplayName("Auto lock on minimize"),
     Description("Require master password when restoring the app after minimize."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter))]
    public bool AutoLockOnMinimize
    {
        get => _autoLockOnMinimize;
        set => SetField(ref _autoLockOnMinimize, value, nameof(AutoLockOnMinimize));
    }

    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous)),
     Browsable(true),
     DisplayName("Two-Factor Authentication (TOTP)"),
     Description("Require a TOTP code from an authenticator app in addition to the master password."),
     TypeConverter(typeof(MiscTools.YesNoTypeConverter))]
    public bool TotpEnabled
    {
        get => _totpEnabled;
        set => SetField(ref _totpEnabled, value, nameof(TotpEnabled));
    }

    [Browsable(false)]
    public string TotpSecret
    {
        get => _totpSecret;
        set => SetField(ref _totpSecret, value ?? "", nameof(TotpSecret));
    }

    [Browsable(false)]
    public string PasswordString
    {
        get => (Password && !string.IsNullOrEmpty(_customPassword)) ? _customPassword : DefaultPassword;
        set
        {
            SetField(ref _customPassword, value ?? "", nameof(PasswordString));
            Password = !string.IsNullOrEmpty(value) && _customPassword != DefaultPassword;
        }
    }

    [Browsable(false)] public string DefaultPassword { get; } = Security.ConnectionFileDefaults.LegacyEncryptionKey;

    /// <summary>
    /// How this store is written, and therefore what else can read it.
    /// </summary>
    /// <remarks>
    /// A property of the store rather than of the application or a global setting, so that opening a
    /// file in a newer build cannot decide it. Classic until the user asks otherwise, and a file that
    /// records nothing is classic — which is every file written before this existed, and every file
    /// upstream mRemoteNG has ever written.
    /// </remarks>
    [Browsable(false)]
    public Security.StorageFormatLevel StorageFormat { get; set; } = Security.StorageFormatLevel.Classic;

    /// <summary>
    /// The level, shown where the store's other security settings already are.
    /// </summary>
    /// <remarks>
    /// On the root node rather than in the options dialog, because the level is a property of this
    /// store and the options dialog is a property of the application — and because a user who has
    /// to go looking in options to find out whether their file is readable by anything else will
    /// not go looking. Read-only: raising it is a decision with a confirmation attached, not a
    /// dropdown.
    /// </remarks>
    [LocalizedAttributes.LocalizedCategory(nameof(Language.Miscellaneous)),
     Browsable(true),
     ReadOnly(true),
     DisplayName("Storage Format"),
     Description("Classic stores can be opened by upstream mRemoteNG. Hardened stores cannot.")]
    public string StorageFormatDisplay => Security.StorageFormat.Describe(StorageFormat);

    [Browsable(false)]
    public bool IsPasswordMatch(SecureString? providedPassword)
    {
        if (providedPassword == null)
            return false;

        string expectedPassword = string.IsNullOrEmpty(_customPassword) ? DefaultPassword : _customPassword;
        string suppliedPassword = providedPassword.ConvertToUnsecureString();
        return string.Equals(expectedPassword, suppliedPassword, StringComparison.Ordinal);
    }

    [Browsable(false)] public RootNodeType Type { get; set; } = rootType;

    public override TreeNodeType GetTreeNodeType()
    {
        return Type == RootNodeType.Connection
            ? TreeNodeType.Root
            : TreeNodeType.PuttyRoot;
    }

    [Browsable(false)]
    public string Filename { get; set; } = string.Empty;
    #endregion
}