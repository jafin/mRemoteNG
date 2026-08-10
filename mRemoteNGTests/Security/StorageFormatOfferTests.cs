using System.IO;
using mRemoteNG.Security;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

/// <summary>
/// Who gets offered the upgrade, and whether a decline sticks.
/// </summary>
[TestFixture]
public class StorageFormatOfferTests
{
    private string _directory;
    private string _storePath;
    private SidecarStorageFormatOfferLog _log;

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-offer-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "confCons.xml");
        File.WriteAllText(_storePath, "<Connections />");
        _log = new SidecarStorageFormatOfferLog();
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void AClassicConnectionFileIsOffered()
    {
        Assert.That(
            StorageFormatOffer.ShouldOffer(StorageFormatLevel.Classic, StorageFormatStoreKind.ConnectionFile, false),
            Is.True);
    }

    [Test]
    public void AHardenedStoreIsNotOffered()
    {
        // Nothing left to offer it.
        Assert.That(
            StorageFormatOffer.ShouldOffer(StorageFormatLevel.Hardened, StorageFormatStoreKind.ConnectionFile, false),
            Is.False);
    }

    [Test]
    public void AClassicSqlStoreIsNotOffered()
    {
        // The upgrade is decided by whoever administers the database, not by whoever opens the
        // application first. An accepting click from someone without that authority costs their
        // colleagues access.
        Assert.That(
            StorageFormatOffer.ShouldOffer(StorageFormatLevel.Classic, StorageFormatStoreKind.SqlDatabase, false),
            Is.False);
    }

    [Test]
    public void ADeclinedFileIsNotOfferedAgain()
    {
        Assert.That(
            StorageFormatOffer.ShouldOffer(StorageFormatLevel.Classic, StorageFormatStoreKind.ConnectionFile, true),
            Is.False);
    }

    [Test]
    public void ADeclineIsRememberedForThatFile()
    {
        Assert.That(_log.WasDeclined(_storePath), Is.False, "nothing has been declined yet");

        Assert.That(_log.RecordDecline(_storePath), Is.True);
        Assert.That(_log.WasDeclined(_storePath), Is.True);
    }

    [Test]
    public void ADeclineDoesNotFollowTheUserToADifferentFile()
    {
        // Per store, because someone with a personal file and a team file has two decisions to make
        // and the second one should still be offered.
        string otherStore = Path.Combine(_directory, "other.xml");
        File.WriteAllText(otherStore, "<Connections />");

        _log.RecordDecline(_storePath);

        Assert.That(_log.WasDeclined(otherStore), Is.False);
    }

    [Test]
    public void DecliningTwiceIsNotAnError()
    {
        // The offer can reappear if the record could not be read, so recording it again has to be
        // harmless rather than throwing on an existing sidecar.
        _log.RecordDecline(_storePath);

        Assert.That(_log.RecordDecline(_storePath), Is.True);
        Assert.That(_log.WasDeclined(_storePath), Is.True);
    }

    [Test]
    public void TheRecordIsNotWrittenIntoTheStore()
    {
        // A classic store has to stay byte-compatible with what upstream mRemoteNG writes. Recording
        // the dismissal inside it would be exactly the construct that rule exists to keep out.
        string before = File.ReadAllText(_storePath);

        _log.RecordDecline(_storePath);

        Assert.That(File.ReadAllText(_storePath), Is.EqualTo(before));
    }

    [Test]
    public void TheLevelIsReportedForBothKindsOfStore()
    {
        RootNodeInfo root = new(RootNodeType.Connection);

        Assert.That(root.StorageFormatDisplay, Is.EqualTo("Classic"));

        root.StorageFormat = StorageFormatLevel.Hardened;

        Assert.That(root.StorageFormatDisplay, Is.EqualTo("Hardened"));
    }
}
