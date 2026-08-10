using mRemoteNG.App;
using mRemoteNG.Config.Connections;
using mRemoteNG.Security;
using NUnit.Framework;

namespace mRemoteNGTests.App;

/// <summary>
/// The decision behind the export warning, separated from the dialog that shows it so it can be
/// tested at all. What the user sees is a message box; what decides whether they see it is this.
/// </summary>
[TestFixture]
public class ExportWeakensProtectionTests
{
    [Test]
    public void AHardenedStoreExportedAsAConnectionFileIsWarnedAbout()
    {
        // The one case the warning exists for: the copy is written classic so upstream can open it,
        // which is exactly what makes it weaker than the store it came from.
        Assert.That(Export.ExportWeakensProtection(StorageFormatLevel.Hardened, SaveFormat.mRXML), Is.True);
    }

    [Test]
    public void AClassicStoreIsNotWarnedAbout()
    {
        // The copy carries the store's own protection. Saying it is weaker would be false, and a
        // warning shown on every export is one users learn to dismiss before reading.
        Assert.That(Export.ExportWeakensProtection(StorageFormatLevel.Classic, SaveFormat.mRXML), Is.False);
    }

    [TestCase(SaveFormat.mRCSV)]
    [TestCase(SaveFormat.mRJSON)]
    [TestCase(SaveFormat.RDP)]
    public void TheInterchangeFormatsAreNotWarnedAbout(SaveFormat saveFormat)
    {
        // These carry no protection whatever the store's level, so what they lose is not a property
        // of the level and is not this message to make.
        Assert.That(Export.ExportWeakensProtection(StorageFormatLevel.Hardened, saveFormat), Is.False);
    }
}
