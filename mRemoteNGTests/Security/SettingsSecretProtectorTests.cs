using System;
using System.Diagnostics;
using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

[TestFixture]
public class SettingsSecretProtectorTests
{
    private SecureString _key;
    private SettingsSecretProtector _protector;

    [SetUp]
    public void Setup()
    {
        _key = "someKey".ConvertToSecureString();
        _protector = new SettingsSecretProtector();
    }

    [Test]
    public void ProtectedValuesRoundTrip()
    {
        string cipherText = _protector.Protect("hunter2", _key);

        Assert.Multiple(() =>
        {
            Assert.That(cipherText, Is.Not.EqualTo("hunter2"));
            Assert.That(_protector.Unprotect(cipherText, _key), Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void ProtectMarksWhatItWrites() =>
        Assert.That(SettingsSecretProtector.IsProtected(_protector.Protect("hunter2", _key)));

    [Test]
    public void AnUnmarkedValueIsReadWithTheLegacyProvider()
    {
        // Every settings secret written before this existed looks like this, as does anything an
        // administrator provisioned through the registry using the password generator.
        LegacyRijndaelCryptographyProvider legacy = new();
        string legacyCipherText = legacy.Encrypt("hunter2", _key);

        Assert.Multiple(() =>
        {
            Assert.That(SettingsSecretProtector.IsProtected(legacyCipherText), Is.False);
            Assert.That(_protector.Unprotect(legacyCipherText, _key), Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public void AnUnrecognisedMarkerFailsRatherThanFallingBack()
    {
        // Falling back would be worse than failing: the legacy provider is unauthenticated, so it
        // would return plausible bytes and the caller would send a wrong password to a server.
        Assert.Throws<EncryptionException>(() => _protector.Unprotect("aead9:abcdef", _key));
    }

    [TestCase("")]
    [TestCase(null)]
    public void EmptyValuesAreHandledWithoutCallingAProvider(string? value)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_protector.Protect(value, _key), Is.Empty);
            Assert.That(_protector.Unprotect(value, _key), Is.Empty);
        });
    }

    [Test]
    public void ARoundTripSurvivesANewProtectorInstance()
    {
        string cipherText = _protector.Protect("hunter2", _key);

        Assert.That(new SettingsSecretProtector().Unprotect(cipherText, _key), Is.EqualTo("hunter2"));
    }

    /// <summary>
    /// These secrets are read on connection paths, so the key-derivation cost has to be paid once
    /// rather than per read. A shared instance is what makes that true; this asserts the sharing
    /// actually works rather than trusting it.
    /// </summary>
    [Test]
    public void RepeatedReadsReuseTheDerivedKey()
    {
        string cipherText = _protector.Protect("hunter2", _key);

        Stopwatch first = Stopwatch.StartNew();
        _protector.Unprotect(cipherText, _key);
        first.Stop();

        Stopwatch second = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
            _protector.Unprotect(cipherText, _key);
        second.Stop();

        TestContext.Out.WriteLine($"first read {first.ElapsedMilliseconds} ms, 20 further reads {second.ElapsedMilliseconds} ms");

        // Twenty cached reads must cost far less than one uncached derivation. Generous factor
        // because CI machines are noisy; the failure this guards against is orders of magnitude.
        Assert.That(second.ElapsedMilliseconds, Is.LessThan(Math.Max(first.ElapsedMilliseconds, 1) * 5),
            "the derived key is not being reused across reads");
    }
}
