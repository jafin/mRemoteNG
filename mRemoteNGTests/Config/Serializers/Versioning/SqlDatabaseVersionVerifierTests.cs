using System;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.Versioning;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Serializers.Versioning;

[TestFixture]
public class SqlDatabaseVersionVerifierTests
{
    private SqlDatabaseVersionVerifier _verifier;

    // The highest version this build knows about. Kept here rather than read from the verifier so
    // that raising it in production code fails these tests and forces a decision.
    //
    // It did exactly that when authenticated encryption arrived: 3.6 used to be "newer than
    // supported" and is now the top of the supported range. The decision it forced is below —
    // two versions are readable, not one.
    private static readonly Version SupportedVersion = new(3, 6);

    // The schema both readable versions share. 3.5 and 3.6 differ only in how the secret columns
    // are encrypted, which is why the verifier accepts a range where it used to accept one value.
    private static readonly Version SchemaVersion = new(3, 5);

    [SetUp]
    public void Setup() => _verifier = new SqlDatabaseVersionVerifier(Substitute.For<IDatabaseConnector>());

    [Test]
    public void AVersionAboveTheSupportedOneIsNewer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_verifier.IsNewerThanSupported(new Version(3, 7)));
            Assert.That(_verifier.IsNewerThanSupported(new Version(4, 0)));
        });
    }

    [Test]
    public void TheSupportedVersionIsNotNewer() =>
        Assert.That(_verifier.IsNewerThanSupported(SupportedVersion), Is.False);

    [Test]
    public void AnOlderVersionIsNotNewer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_verifier.IsNewerThanSupported(new Version(3, 4)), Is.False);
            Assert.That(_verifier.IsNewerThanSupported(new Version(2, 2)), Is.False);
        });
    }

    [Test]
    public void ANullVersionIsNotNewer() =>
        Assert.That(_verifier.IsNewerThanSupported(null!), Is.False);

    [Test]
    public void VerifyingANewerVersionFails()
    {
        // It must not reach the upgraders: none of them can downgrade a database, and the caller
        // needs this case distinguished from "too old to upgrade" so it can refuse rather than read.
        Assert.That(_verifier.VerifyDatabaseVersion(new Version(3, 7)), Is.False);
    }

    [Test]
    public void VerifyingTheSupportedVersionSucceeds() =>
        Assert.That(_verifier.VerifyDatabaseVersion(SupportedVersion));

    [Test]
    public void TheSchemaVersionIsStillReadable()
    {
        // Every database in the field is at this version, and none of them is out of date in any
        // sense the upgrader chain understands — there is nothing to upgrade, only a different way
        // of encrypting the secret columns. An equality check against the highest version would
        // have reported all of them as unsupported.
        Assert.Multiple(() =>
        {
            Assert.That(_verifier.VerifyDatabaseVersion(SchemaVersion));
            Assert.That(_verifier.IsNewerThanSupported(SchemaVersion), Is.False);
        });
    }

    [Test]
    public void TheTwoReadableVersionsAreAdjacent()
    {
        // Nothing sits between them, which is what makes "below the AEAD version" and "at the
        // schema version" the same statement everywhere else in this change.
        Assert.That(SqlDatabaseVersionVerifier.SchemaVersion, Is.EqualTo(SchemaVersion));
        Assert.That(SqlDatabaseVersionVerifier.HighestSupportedVersion, Is.EqualTo(SupportedVersion));
    }
}
