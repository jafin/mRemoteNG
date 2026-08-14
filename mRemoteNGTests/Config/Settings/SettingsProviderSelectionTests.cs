using System.Configuration;
using System.IO;
using System.Windows.Forms;
using mRemoteNG.App.Info;
using mRemoteNG.Config.Settings.Providers;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Settings;

/// <summary>
/// Which settings provider the application wires up, and why it is the same one for both editions.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a regression that reached a user. When the portable edition became a
/// runtime answer, the installed edition was pointed at <see cref="LocalFileSettingsProvider"/> —
/// the base class <c>ChooseProvider</c> had always compiled to when <c>PORTABLE</c> was undefined.
/// Nothing had ever run that path, because every build configuration defined <c>PORTABLE</c>, and
/// it turns out to persist nothing at all: no <c>user.config</c> is written and every setting
/// returns to its default at the next start.
/// </para>
/// <para>
/// The symptom was the update-settings prompt appearing at every launch however it was answered,
/// which is a long way from the change that caused it. The provider choice is asserted here so the
/// next person to touch it meets a failing test rather than that.
/// </para>
/// </remarks>
[TestFixture]
public class SettingsProviderSelectionTests
{
    [TearDown]
    public void Teardown() => PortableEdition.OverrideForTests(null);

    [Test]
    public void TheFileBasedProviderIsUsedForBothEditions()
    {
        PortableEdition.OverrideForTests(true);
        SettingsProvider portable = PortableSettingsInitializer.CreateProvider();

        PortableEdition.OverrideForTests(false);
        SettingsProvider installed = PortableSettingsInitializer.CreateProvider();

        Assert.Multiple(() =>
        {
            Assert.That(portable, Is.TypeOf<PortableSettingsProvider>());
            Assert.That(installed, Is.TypeOf<PortableSettingsProvider>(),
                "the installed edition persists nothing through LocalFileSettingsProvider - see the class remarks");
        });
    }

    [Test]
    public void TheProviderIsNotLocalFileSettingsProvider()
    {
        // Named separately from the assertion above because this is the specific mistake, and a
        // failure here should say so rather than leaving the reader to work out which type is which.
        PortableEdition.OverrideForTests(false);

        Assert.That(PortableSettingsInitializer.CreateProvider(), Is.Not.InstanceOf<LocalFileSettingsProvider>());
    }

    [Test]
    public void TheEditionDecidesWhereSettingsGoRatherThanWhichProviderWritesThem()
    {
        // The property that makes one provider correct for both: the path follows the edition, so
        // the provider does not have to. If these two ever agree, the edition has stopped selecting
        // a location and something else has to.
        PortableEdition.OverrideForTests(true);
        string portablePath = SettingsFileInfo.SettingsPath;

        PortableEdition.OverrideForTests(false);
        string installedPath = SettingsFileInfo.SettingsPath;

        Assert.That(portablePath, Is.Not.EqualTo(installedPath));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheReportedSettingsFileIsTheOneTheProviderWrites(bool isPortable)
    {
        // The startup log, the debug report and the load message all name UserSettingsFilePath, so
        // it has to be the file PortableSettingsProvider uses: <SettingsPath>\<exe>.settings. It
        // used to report the framework's user.config for the installed edition, which nothing writes
        // now that both editions share the file-based provider — a diagnostic saying "file does not
        // exist - using defaults" about settings that were persisting perfectly well.
        PortableEdition.OverrideForTests(isPortable);

        string expected = Path.Combine(
            SettingsFileInfo.SettingsPath,
            $"{Path.GetFileNameWithoutExtension(Application.ExecutablePath)}.settings");

        Assert.Multiple(() =>
        {
            Assert.That(SettingsFileInfo.UserSettingsFilePath, Is.EqualTo(expected));
            Assert.That(SettingsFileInfo.UserSettingsFilePath, Does.Not.EndWith("user.config"));
        });
    }
}
