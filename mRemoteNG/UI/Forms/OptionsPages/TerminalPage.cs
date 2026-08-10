using System;
using System.Globalization;
using System.Runtime.Versioning;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.UI.Forms.OptionsPages;

/// <summary>
/// Appearance and input options for the native SSH terminal.
/// </summary>
/// <remarks>
/// Applies to new sessions. Restyling a live terminal mid-session is possible but would change
/// what is on screen underneath someone's work, so the settings take effect on the next connection.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class TerminalPage
{
    /// <summary>Stored values for the colour scheme, kept out of the display strings so a
    /// translated UI does not change what is written to the settings file.</summary>
    private static readonly string[] ColorSchemeValues = ["Follow", "Dark", "Light"];

    public TerminalPage()
    {
        InitializeComponent();
        ApplyTheme();
        PageIcon = Resources.ImageConverter.GetImageAsIcon(Properties.Resources.Console_16x);
    }

    public override string PageName
    {
        get => Language.SshNativeTerminalOptions;
        set { }
    }

    public override void ApplyLanguage()
    {
        base.ApplyLanguage();

        lblIntro.Text = Language.SshNativeOptionsIntro;
        lblFontFamily.Text = Language.SshNativeOptionsFont;
        lblFontSize.Text = Language.SshNativeOptionsFontSize;
        lblColorScheme.Text = Language.SshNativeOptionsColorScheme;
        lblScrollback.Text = Language.SshNativeOptionsScrollback;
        chkCtrlVPastes.Text = Language.SshNativeOptionsCtrlVPastes;
        lblCtrlVNote.Text = Language.SshNativeOptionsCtrlVNote;

        int selected = cboColorScheme.SelectedIndex;
        cboColorScheme.Items.Clear();
        cboColorScheme.Items.AddRange(
        [
            Language.SshNativeOptionsSchemeFollow,
            Language.SshNativeOptionsSchemeDark,
            Language.SshNativeOptionsSchemeLight
        ]);
        cboColorScheme.SelectedIndex = selected >= 0 && selected < cboColorScheme.Items.Count ? selected : 0;
    }

    public override void LoadSettings()
    {
        Properties.OptionsTerminalPage settings = Properties.OptionsTerminalPage.Default;

        txtFontFamily.Text = settings.TerminalFontFamily;
        numFontSize.Value = Clamp(settings.TerminalFontSize, numFontSize.Minimum, numFontSize.Maximum);
        numScrollback.Value = Clamp(settings.TerminalScrollback, numScrollback.Minimum, numScrollback.Maximum);
        chkCtrlVPastes.Checked = settings.TerminalCtrlVPastes;

        // Case-insensitively, matching how ProtocolNativeSsh.ResolveColorScheme reads the same
        // value. A hand-edited "dark" already renders a dark terminal; showing "Follow" here would
        // make simply opening the page and clicking OK rewrite the setting to something else.
        int index = Array.FindIndex(ColorSchemeValues,
            value => string.Equals(value, settings.TerminalColorScheme, StringComparison.OrdinalIgnoreCase));
        cboColorScheme.SelectedIndex = index >= 0 ? index : 0;
    }

    public override void SaveSettings()
    {
        base.SaveSettings();

        Properties.OptionsTerminalPage settings = Properties.OptionsTerminalPage.Default;

        // An empty font would leave the terminal with no fallback at all; the default is a list
        // ending in "monospace", which always resolves to something.
        settings.TerminalFontFamily = string.IsNullOrWhiteSpace(txtFontFamily.Text)
            ? "Cascadia Mono, Consolas, monospace"
            : txtFontFamily.Text.Trim();

        settings.TerminalFontSize = (int)numFontSize.Value;
        settings.TerminalScrollback = (int)numScrollback.Value;
        settings.TerminalCtrlVPastes = chkCtrlVPastes.Checked;
        settings.TerminalColorScheme = ColorSchemeValues[
            cboColorScheme.SelectedIndex >= 0 ? cboColorScheme.SelectedIndex : 0];
    }

    /// <summary>
    /// Keeps a value the user could have hand-edited in the settings file inside the range the
    /// control accepts. Assigning outside it throws, which would take the whole options form down.
    /// </summary>
    private static decimal Clamp(int value, decimal minimum, decimal maximum) =>
        Math.Clamp(Convert.ToDecimal(value, CultureInfo.InvariantCulture), minimum, maximum);
}
