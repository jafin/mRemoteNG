using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.Authentication;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using NUnit.Framework;


namespace mRemoteNGTests.Security.Authentication;

public class PasswordAuthenticatorTests
{
    private ICryptographyProvider _cryptographyProvider;
    private string _cipherText;
    private readonly SecureString _correctPassword = "9theCorrectPass#5".ConvertToSecureString();
    private readonly SecureString _wrongPassword = "wrongPassword".ConvertToSecureString();

    [SetUp]
    public void Setup()
    {
        _cryptographyProvider = new AeadCryptographyProvider {KeyDerivationIterations = 10000};
        _cipherText = "MPELiwk7+xeNlruIyt5uxTvVB+/RLVoLdUGnwY4CWCqwKe7T2IBwWo4oaKum5hdv7447g5m2nZsYPrfARSlotQB4r1KZQg==";
    }

    [Test]
    public void AuthenticatingWithCorrectPasswordReturnsTrue()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => Optional<SecureString>.Empty);
        var authenticated = authenticator.Authenticate(_correctPassword);
        Assert.That(authenticated);
    }

    [Test]
    public void AuthenticatingWithWrongPasswordReturnsFalse()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => Optional<SecureString>.Empty);
        var authenticated = authenticator.Authenticate(_wrongPassword);
        Assert.That(!authenticated);
    }

    [Test]
    public void AuthenticationRequestorIsCalledWhenInitialPasswordIsWrong()
    {
        var wasCalled = false;

        Optional<SecureString> AuthenticationRequestor()
        {
            wasCalled = true;
            return _correctPassword;
        }

        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, AuthenticationRequestor);
        authenticator.Authenticate(_wrongPassword);
        Assert.That(wasCalled);
    }

    [Test]
    public void AuthenticationRequestorNotCalledWhenInitialPasswordIsCorrect()
    {
        var wasCalled = false;
        Optional<SecureString> AuthenticationRequestor()
        {
            wasCalled = true;
            return _correctPassword;
        }

        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, AuthenticationRequestor);
        authenticator.Authenticate(_correctPassword);
        Assert.That(!wasCalled);
    }

    [Test]
    public void ProvidingCorrectPasswordToTheAuthenticationRequestorReturnsTrue()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => _correctPassword);
        var authenticated = authenticator.Authenticate(_wrongPassword);
        Assert.That(authenticated);
    }

    [Test]
    public void AuthenticationFailsWhenAuthenticationRequestorGivenEmptyPassword()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => new SecureString());
        var authenticated = authenticator.Authenticate(_wrongPassword);
        Assert.That(!authenticated);
    }

    [Test]
    public void AuthenticatorRespectsMaxAttempts()
    {
        var authAttempts = 0;
        Optional<SecureString>  AuthenticationRequestor()
        {
            authAttempts++;
            return _wrongPassword;
        }

        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, AuthenticationRequestor);
        authenticator.Authenticate(_wrongPassword);
        Assert.That(authAttempts == authenticator.MaxAttempts);
    }

    [Test]
    public void AuthenticatorRespectsMaxAttemptsCustomValue()
    {
        const int customMaxAttempts = 5;
        var authAttempts = 0;
        Optional<SecureString> AuthenticationRequestor()
        {
            authAttempts++;
            return _wrongPassword;
        }

        var authenticator =
            new PasswordAuthenticator(_cryptographyProvider, _cipherText, AuthenticationRequestor)
            {
                MaxAttempts = customMaxAttempts
            };
        authenticator.Authenticate(_wrongPassword);
        Assert.That(authAttempts == customMaxAttempts);
    }

    [Test]
    public void NoPlaintextValidatorAcceptsAnyDecryptionThatSucceeds()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => Optional<SecureString>.Empty);
        Assert.That(authenticator.PlaintextValidator, Is.Null, "the validator is opt-in");
        Assert.That(authenticator.Authenticate(_correctPassword));
    }

    [Test]
    public void PlaintextValidatorRejectingTheDecryptedValueFailsAuthentication()
    {
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => Optional<SecureString>.Empty)
        {
            PlaintextValidator = _ => false
        };

        Assert.That(authenticator.Authenticate(_correctPassword), Is.False,
            "the password decrypts, so only the plaintext check can reject it");
        Assert.That(authenticator.LastAuthenticatedPassword, Is.Null);
    }

    [Test]
    public void PlaintextValidatorAcceptingTheDecryptedValueAuthenticates()
    {
        string? seen = null;
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, () => Optional<SecureString>.Empty)
        {
            PlaintextValidator = plainText => { seen = plainText; return true; }
        };

        Assert.That(authenticator.Authenticate(_correctPassword));
        Assert.That(seen, Is.Not.Null, "the validator receives the decrypted plaintext");
    }

    [Test]
    public void RejectedPlaintextConsumesAnAttemptAndReprompts()
    {
        var authAttempts = 0;
        Optional<SecureString> AuthenticationRequestor()
        {
            authAttempts++;
            return _correctPassword;
        }

        // The password is right every time; only the plaintext check refuses. A rejection has to
        // behave like a wrong password — re-prompt, then stop at MaxAttempts — or a store whose
        // sentinel never matches would loop forever.
        var authenticator = new PasswordAuthenticator(_cryptographyProvider, _cipherText, AuthenticationRequestor)
        {
            PlaintextValidator = _ => false
        };

        Assert.That(authenticator.Authenticate(_correctPassword), Is.False);
        Assert.That(authAttempts, Is.EqualTo(authenticator.MaxAttempts));
    }
}