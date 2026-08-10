using System.Security.Cryptography;
using mRemoteNG.Security.KeyDerivation;
using NUnit.Framework;

namespace mRemoteNGTests.Security.KeyDerivation;

[TestFixture]
public class KeyDerivationPrfTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("MD5")]
    [TestCase("something this build has never heard of")]
    public void AnythingUnusableReadsAsSha1(string? recorded)
    {
        // Absence has to keep meaning SHA-1 or every file written before this change stops opening.
        // A value this build does not know is in the same position: the safest reading is the one
        // that was true for fifteen years.
        Assert.That(KeyDerivationPrf.Parse(recorded), Is.EqualTo(HashAlgorithmName.SHA1));
    }

    [TestCase("SHA256")]
    [TestCase("sha256")]
    public void ARecordedFunctionIsHonoured(string recorded) =>
        Assert.That(KeyDerivationPrf.Parse(recorded), Is.EqualTo(HashAlgorithmName.SHA256));

    [Test]
    public void Sha1RecordsNothing()
    {
        // Writing it would put an attribute upstream mRemoteNG has never seen into a file it is
        // supposed to still be able to read.
        Assert.That(KeyDerivationPrf.ToRecordedValue(HashAlgorithmName.SHA1), Is.Null);
    }

    [Test]
    public void TheHardenedFunctionRoundTrips()
    {
        string? recorded = KeyDerivationPrf.ToRecordedValue(KeyDerivationPrf.Hardened);

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.Not.Null);
            Assert.That(KeyDerivationPrf.Parse(recorded), Is.EqualTo(KeyDerivationPrf.Hardened));
        });
    }

    [Test]
    public void TheDefaultIsSha1AndTheHardenedFunctionIsNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyDerivationPrf.Default, Is.EqualTo(HashAlgorithmName.SHA1));
            Assert.That(KeyDerivationPrf.Hardened, Is.Not.EqualTo(KeyDerivationPrf.Default));
        });
    }

    [Test]
    public void OnlyFunctionsTheFormatCanRecordAreSupported()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeyDerivationPrf.IsSupported(HashAlgorithmName.SHA1));
            Assert.That(KeyDerivationPrf.IsSupported(HashAlgorithmName.SHA256));
            Assert.That(KeyDerivationPrf.IsSupported(HashAlgorithmName.SHA512));
            Assert.That(KeyDerivationPrf.IsSupported(HashAlgorithmName.MD5), Is.False);
        });
    }
}
