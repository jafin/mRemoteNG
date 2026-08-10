using System;
using System.Linq;
using System.Security;
using mRemoteNG.Tools;

namespace mRemoteNG.Security.Authentication;

public class PasswordAuthenticator : IAuthenticator
{
    private readonly ICryptographyProvider _cryptographyProvider;
    private readonly string _cipherText;
    private readonly Func<Optional<SecureString>> _authenticationRequestor;

    public PasswordAuthenticator(ICryptographyProvider cryptographyProvider,
        string cipherText,
        Func<Optional<SecureString>> authenticationRequestor)
    {
        ArgumentNullException.ThrowIfNull(cryptographyProvider);
        ArgumentNullException.ThrowIfNull(authenticationRequestor);
        if (string.IsNullOrEmpty(cipherText))
            throw new ArgumentException("Value cannot be null or empty.", nameof(cipherText));
        _cryptographyProvider = cryptographyProvider;
        _cipherText = cipherText;
        _authenticationRequestor = authenticationRequestor;
    }

    public int MaxAttempts { get; set; } = 3;
    public SecureString? LastAuthenticatedPassword { get; private set; }

    /// <summary>
    /// Optional check on the decrypted plaintext. When set, a password is accepted only if
    /// decryption succeeds <i>and</i> the result satisfies this.
    /// </summary>
    /// <remarks>
    /// Without it, success means "decryption did not throw". That is sound for an authenticated
    /// cipher, where a wrong key fails the tag, and unsound for AES-CBC with PKCS7, where a wrong
    /// key produces valid padding roughly once in 256 attempts and returns arbitrary bytes. Callers
    /// whose ciphertext has known plaintext should supply it; the check itself belongs to the
    /// caller, which is the only thing that knows what it encrypted.
    /// </remarks>
    public Func<string, bool>? PlaintextValidator { get; set; }

    public bool Authenticate(SecureString password)
    {
        bool authenticated = false;
        int attempts = 0;
        while (!authenticated && attempts < MaxAttempts)
        {
            if (IsAccepted(password))
            {
                authenticated = true;
                LastAuthenticatedPassword = password;
            }
            else
            {
                Optional<SecureString> providedPassword = _authenticationRequestor();
                if (!providedPassword.Any())
                    return false;

                password = providedPassword.First();
                if (password == null || password.Length == 0) break;
            }

            attempts++;
        }

        return authenticated;
    }

    /// <summary>
    /// A rejected plaintext is treated exactly like a failed decryption — it consumes an attempt and
    /// re-prompts — because to the user they are the same event: the password did not work.
    /// </summary>
    private bool IsAccepted(SecureString password)
    {
        try
        {
            string plainText = _cryptographyProvider.Decrypt(_cipherText, password);
            return PlaintextValidator is null || PlaintextValidator(plainText);
        }
        catch
        {
            return false;
        }
    }
}