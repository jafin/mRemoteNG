using System.Runtime.Versioning;
using System.Threading;
using mRemoteNG.UI.Forms.OptionsPages;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms.OptionsPages;

/// <summary>
/// The terminal options page, round-tripped through its own settings store.
/// </summary>
/// <remarks>
/// Exercises the page directly rather than through <c>FrmOptions</c>: that form leaks native
/// resources badly enough that its own suite is limited to one test per fixture.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
[SupportedOSPlatform("windows")]
public class TerminalPageTests
{
    private string _font = string.Empty;
    private int _fontSize;
    private int _scrollback;
    private string _scheme = string.Empty;
    private bool _ctrlV;

    [SetUp]
    public void SaveRealSettings()
    {
        mRemoteNG.Properties.OptionsTerminalPage settings = mRemoteNG.Properties.OptionsTerminalPage.Default;
        _font = settings.TerminalFontFamily;
        _fontSize = settings.TerminalFontSize;
        _scrollback = settings.TerminalScrollback;
        _scheme = settings.TerminalColorScheme;
        _ctrlV = settings.TerminalCtrlVPastes;
    }

    [TearDown]
    public void RestoreRealSettings()
    {
        // These are the developer's own settings; the test must not leave its values behind.
        mRemoteNG.Properties.OptionsTerminalPage settings = mRemoteNG.Properties.OptionsTerminalPage.Default;
        settings.TerminalFontFamily = _font;
        settings.TerminalFontSize = _fontSize;
        settings.TerminalScrollback = _scrollback;
        settings.TerminalColorScheme = _scheme;
        settings.TerminalCtrlVPastes = _ctrlV;
    }

    [Test]
    public void CtrlVPastesByDefault()
    {
        // The Windows habit, and the explicit product decision. Changing this default silently
        // would break paste for everyone who never opens the options page.
        Assert.That(
            new mRemoteNG.Properties.OptionsTerminalPage().TerminalCtrlVPastes, Is.True);
    }

    [Test]
    public void SettingsRoundTripThroughThePage()
    {
        using TerminalPage page = new();
        page.ApplyLanguage();

        mRemoteNG.Properties.OptionsTerminalPage settings = mRemoteNG.Properties.OptionsTerminalPage.Default;
        settings.TerminalFontFamily = "Fira Code";
        settings.TerminalFontSize = 18;
        settings.TerminalScrollback = 12000;
        settings.TerminalColorScheme = "Light";
        settings.TerminalCtrlVPastes = false;

        page.LoadSettings();

        // Round-trip through the controls, not just the store, so a control the page forgot to
        // bind shows up as a lost value rather than passing silently.
        settings.TerminalFontFamily = "wrong";
        settings.TerminalFontSize = 8;
        settings.TerminalScrollback = 1;
        settings.TerminalColorScheme = "Dark";
        settings.TerminalCtrlVPastes = true;

        page.SaveSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.TerminalFontFamily, Is.EqualTo("Fira Code"));
            Assert.That(settings.TerminalFontSize, Is.EqualTo(18));
            Assert.That(settings.TerminalScrollback, Is.EqualTo(12000));
            Assert.That(settings.TerminalColorScheme, Is.EqualTo("Light"));
            Assert.That(settings.TerminalCtrlVPastes, Is.False);
        });
    }

    [Test]
    public void AnEmptyFontFallsBackRatherThanLeavingTheTerminalWithNone()
    {
        using TerminalPage page = new();
        page.ApplyLanguage();
        page.LoadSettings();

        page.FindTextBox().Text = "   ";
        page.SaveSettings();

        Assert.That(mRemoteNG.Properties.OptionsTerminalPage.Default.TerminalFontFamily,
            Does.Contain("monospace"));
    }

    [Test]
    public void AnOutOfRangeStoredValueIsClampedRatherThanThrowing()
    {
        // The settings file is hand-editable, and assigning outside a NumericUpDown's range
        // throws — which would take the whole options form down on open.
        mRemoteNG.Properties.OptionsTerminalPage.Default.TerminalFontSize = 9999;
        mRemoteNG.Properties.OptionsTerminalPage.Default.TerminalScrollback = -5;

        using TerminalPage page = new();
        page.ApplyLanguage();

        Assert.DoesNotThrow(page.LoadSettings);
    }

    [Test]
    public void AnUnrecognisedSchemeFallsBackToFollow()
    {
        mRemoteNG.Properties.OptionsTerminalPage.Default.TerminalColorScheme = "chartreuse";

        using TerminalPage page = new();
        page.ApplyLanguage();
        page.LoadSettings();
        page.SaveSettings();

        Assert.That(mRemoteNG.Properties.OptionsTerminalPage.Default.TerminalColorScheme,
            Is.EqualTo("Follow"));
    }
}

internal static class TerminalPageTestExtensions
{
    /// <summary>The font text box, found by name so the test does not depend on control order.</summary>
    public static System.Windows.Forms.TextBox FindTextBox(this TerminalPage page)
    {
        System.Windows.Forms.Control[] found = page.Controls.Find("txtFontFamily", searchAllChildren: true);
        Assert.That(found, Is.Not.Empty, "txtFontFamily is missing from the page");
        return (System.Windows.Forms.TextBox)found[0];
    }
}
