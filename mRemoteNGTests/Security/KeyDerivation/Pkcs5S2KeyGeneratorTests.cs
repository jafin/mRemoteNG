using System;
using mRemoteNG.Security.KeyDerivation;
using NUnit.Framework;


namespace mRemoteNGTests.Security.KeyDerivation;

public class Pkcs5S2KeyGeneratorTests
{
    private const int Iterations = 1000;

    [Test]
    public void ConstructingWithValidParametersThrowsNoException()
    {
        // ReSharper disable once ObjectCreationAsStatement
        Assert.DoesNotThrow(() => new Pkcs5S2KeyGenerator(256, Iterations));
    }

    [Test]
    public void CreatingGeneratorWithLowIterationCountThrowsError()
    {
        // ReSharper disable once ObjectCreationAsStatement
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pkcs5S2KeyGenerator(256, 999));
    }

    [Test]
    public void CreatingGeneratorWithNegativeKeyBitSizeThrowsError()
    {
        // ReSharper disable once ObjectCreationAsStatement
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pkcs5S2KeyGenerator(-1, Iterations));
    }

    [Test]
    public void IdenticalParametersProduceIdenticalKeys()
    {
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, Iterations);
        var key1 = keyDerivationFunction.DeriveKey("", Array.Empty<byte>());
        var key2 = keyDerivationFunction.DeriveKey("", Array.Empty<byte>());
        Assert.That(key1, Is.EquivalentTo(key2));
    }

    [Test]
    public void DifferingIterationsProduceDifferingKeys()
    {
        var keyDerivationFunction1 = new Pkcs5S2KeyGenerator(256, 1001);
        var keyDerivationFunction2 = new Pkcs5S2KeyGenerator(256, 1002);
        var key1 = keyDerivationFunction1.DeriveKey("", Array.Empty<byte>());
        var key2 = keyDerivationFunction2.DeriveKey("", Array.Empty<byte>());
        Assert.That(key1, Is.Not.EquivalentTo(key2));
    }

    [Test]
    public void DifferingKeysizeProduceDifferingKeys()
    {
        var keyDerivationFunction1 = new Pkcs5S2KeyGenerator(256, Iterations);
        var keyDerivationFunction2 = new Pkcs5S2KeyGenerator(512, Iterations);
        var key1 = keyDerivationFunction1.DeriveKey("", Array.Empty<byte>());
        var key2 = keyDerivationFunction2.DeriveKey("", Array.Empty<byte>());
        Assert.That(key1, Is.Not.EquivalentTo(key2));
    }

    [Test]
    public void DifferingPasswordsProduceDifferingKeys()
    {
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, Iterations);
        var key1 = keyDerivationFunction.DeriveKey("a", Array.Empty<byte>());
        var key2 = keyDerivationFunction.DeriveKey("b", Array.Empty<byte>());
        Assert.That(key1, Is.Not.EquivalentTo(key2));
    }

    [Test]
    public void DifferingSaltsProduceDifferingKeys()
    {
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, Iterations);
        var key1 = keyDerivationFunction.DeriveKey("", Array.Empty<byte>());
        var key2 = keyDerivationFunction.DeriveKey("", new byte[] {1});
        Assert.That(key1, Is.Not.EquivalentTo(key2));
    }

    [Test]
    public void PasswordWithSpecialCharactersParagraphSignProducesConsistentKey()
    {
        // Regression test for GitHub issue #2274:
        // Passwords containing non-ASCII characters (e.g. §) must produce
        // the same key on every call so that stored passwords can be
        // decrypted correctly.
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, Iterations);
        var salt = new byte[] { 1, 2, 3, 4 };
        var key1 = keyDerivationFunction.DeriveKey("foo§bar", salt);
        var key2 = keyDerivationFunction.DeriveKey("foo§bar", salt);
        Assert.That(key1, Is.EquivalentTo(key2));
    }

    [Test]
    public void PasswordWithSpecialCharactersParagraphSignProducesDifferentKeyThanWithoutIt()
    {
        // Regression test for GitHub issue #2274:
        // Ensure § is not silently dropped or truncated during key derivation.
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, Iterations);
        var salt = new byte[] { 1, 2, 3, 4 };
        var keyWith = keyDerivationFunction.DeriveKey("foo§bar", salt);
        var keyWithout = keyDerivationFunction.DeriveKey("foobar", salt);
        Assert.That(keyWith, Is.Not.EquivalentTo(keyWithout));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(256)]
    [TestCase(333)]
    public void KeyLengthIsKeyBitSizeDividedBy8(int keyBitSize)
    {
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(keyBitSize, Iterations);
        var key = keyDerivationFunction.DeriveKey("", Array.Empty<byte>());
        Assert.That(key.Length, Is.EqualTo(keyBitSize / 8));
    }

    [Test]
    public void TheDefaultFunctionIsStillSha1()
    {
        // What every connection file written before the format recorded it used, and what upstream
        // mRemoteNG derives with. Changing this default would break both, silently.
        var implicitDefault = new Pkcs5S2KeyGenerator(256, Iterations);
        var explicitSha1 = new Pkcs5S2KeyGenerator(256, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA1);

        Assert.That(implicitDefault.DeriveKey("password", new byte[] { 1, 2, 3, 4 }),
            Is.EquivalentTo(explicitSha1.DeriveKey("password", new byte[] { 1, 2, 3, 4 })));
    }

    [TestCase("SHA1", "6E88BE8BAD7EAE9D9E10AA061224034FED48D03FCBAD968B56006784539D5214")]
    [TestCase("SHA256", "632C2812E46D4604102BA7618E9D6D7D2F8128F6266B4A03264D2A0460B7DCB3")]
    public void DerivationMatchesTheStandardVectors(string function, string expected)
    {
        // PBKDF2 is fixed by RFC 2898, so these are the standard's output rather than ours. The
        // SHA-1 row is what keeps existing connection files readable: if it moves, every stored
        // password in every file written before this release becomes unreachable.
        var keyDerivationFunction = new Pkcs5S2KeyGenerator(256, 1000, new System.Security.Cryptography.HashAlgorithmName(function));

        string derived = Convert.ToHexString(keyDerivationFunction.DeriveKey("password", "salt"u8.ToArray()));

        Assert.That(derived, Is.EqualTo(expected));
    }

    [Test]
    public void DifferingFunctionsProduceDifferingKeys()
    {
        var sha1 = new Pkcs5S2KeyGenerator(256, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA1);
        var sha256 = new Pkcs5S2KeyGenerator(256, Iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);

        Assert.That(sha1.DeriveKey("password", new byte[] { 1, 2, 3, 4 }),
            Is.Not.EquivalentTo(sha256.DeriveKey("password", new byte[] { 1, 2, 3, 4 })));
    }

    [Test]
    public void AnUnsupportedFunctionIsRejected()
    {
        // Deriving with something the file format cannot record would produce a file this build
        // could not read back.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Pkcs5S2KeyGenerator(256, Iterations, System.Security.Cryptography.HashAlgorithmName.MD5));
    }
}
