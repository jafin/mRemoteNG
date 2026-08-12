using System;
using mRemoteNG.Security;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

[TestFixture]
public class StorageFormatTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Classic")]
    public void AnAbsentOrClassicDeclarationReadsAsClassic(string? recorded) =>
        Assert.That(StorageFormat.Resolve(recorded), Is.EqualTo(StorageFormatLevel.Classic));

    [TestCase("Hardened")]
    [TestCase("hardened")]
    [TestCase("HARDENED")]
    public void HardenedIsRecognisedWhateverItsCase(string recorded) =>
        Assert.That(StorageFormat.Resolve(recorded), Is.EqualTo(StorageFormatLevel.Hardened));

    [TestCase("Quantum")]
    [TestCase("something this build has never heard of")]
    [TestCase("Hardened2")]
    public void ADeclarationThisBuildDoesNotKnowResolvesToNoLevelAtAll(string recorded)
    {
        // Not classic. A level that is present but unrecognised says a build that knew more than
        // this one wrote the file deliberately; reading it as classic discards that statement and
        // the next ordinary save writes the file back without it.
        Assert.That(StorageFormat.Resolve(recorded), Is.Null);
    }

    [Test]
    public void AbsenceIsRecognisedAndAnUnknownValueIsNot()
    {
        // The distinction the whole change turns on: absent is a classic declaration, not a missing
        // one, and must keep opening every file upstream mRemoteNG has ever written.
        Assert.Multiple(() =>
        {
            Assert.That(StorageFormat.IsRecognised(null), "absence is how classic is declared");
            Assert.That(StorageFormat.IsRecognised(""), "so is an empty declaration");
            Assert.That(StorageFormat.IsRecognised("Hardened"));
            Assert.That(StorageFormat.IsRecognised("Quantum"), Is.False);
        });
    }

    [Test]
    public void ClassicRecordsNothing()
    {
        // Absence is what means classic. Writing a value would put an attribute upstream mRemoteNG
        // has never seen into a file it is supposed to still be able to read.
        Assert.That(StorageFormat.ToRecordedValue(StorageFormatLevel.Classic), Is.Null);
    }

    [Test]
    public void HardenedRoundTripsThroughItsRecordedValue()
    {
        string? recorded = StorageFormat.ToRecordedValue(StorageFormatLevel.Hardened);

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.Not.Null);
            Assert.That(StorageFormat.Resolve(recorded), Is.EqualTo(StorageFormatLevel.Hardened));
        });
    }

    [Test]
    public void ASqlDatabaseBelowTheHardenedVersionIsClassic()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StorageFormat.ForSqlDatabase(new Version(3, 5)), Is.EqualTo(StorageFormatLevel.Classic));
            Assert.That(StorageFormat.ForSqlDatabase(new Version(2, 2)), Is.EqualTo(StorageFormatLevel.Classic));
            Assert.That(StorageFormat.ForSqlDatabase(null), Is.EqualTo(StorageFormatLevel.Classic));
        });
    }

    [Test]
    public void ASqlDatabaseAtOrAboveTheHardenedVersionIsHardened()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StorageFormat.ForSqlDatabase(StorageFormat.SqlHardenedVersion), Is.EqualTo(StorageFormatLevel.Hardened));
            Assert.That(StorageFormat.ForSqlDatabase(new Version(4, 0)), Is.EqualTo(StorageFormatLevel.Hardened));
        });
    }

    [Test]
    public void EverySqlDatabaseThatExistsTodayIsClassic()
    {
        // The hardened version is reserved here and written by nothing until
        // encrypt-sql-backend-with-aead lands. Until then no database can resolve to hardened, which
        // is correct — none of them are.
        Assert.That(StorageFormat.SqlHardenedVersion, Is.GreaterThan(new Version(3, 5)),
            "the reserved version must be above the highest schema version in use");
    }
}
