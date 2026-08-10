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
    [TestCase("something this build has never heard of")]
    public void AnythingButHardenedReadsAsClassic(string? recorded) =>
        Assert.That(StorageFormat.Parse(recorded), Is.EqualTo(StorageFormatLevel.Classic));

    [TestCase("Hardened")]
    [TestCase("hardened")]
    [TestCase("HARDENED")]
    public void HardenedIsRecognisedWhateverItsCase(string recorded) =>
        Assert.That(StorageFormat.Parse(recorded), Is.EqualTo(StorageFormatLevel.Hardened));

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
            Assert.That(StorageFormat.Parse(recorded), Is.EqualTo(StorageFormatLevel.Hardened));
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
