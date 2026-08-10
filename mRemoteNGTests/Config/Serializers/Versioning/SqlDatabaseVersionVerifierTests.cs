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

    // The highest schema version this build knows about. Kept here rather than read from the
    // verifier so that raising it in production code fails these tests and forces a decision.
    private static readonly Version SupportedVersion = new(3, 5);

    [SetUp]
    public void Setup() => _verifier = new SqlDatabaseVersionVerifier(Substitute.For<IDatabaseConnector>());

    [Test]
    public void AVersionAboveTheSupportedOneIsNewer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_verifier.IsNewerThanSupported(new Version(3, 6)));
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
        Assert.That(_verifier.VerifyDatabaseVersion(new Version(3, 6)), Is.False);
    }

    [Test]
    public void VerifyingTheSupportedVersionSucceeds() =>
        Assert.That(_verifier.VerifyDatabaseVersion(SupportedVersion));
}
