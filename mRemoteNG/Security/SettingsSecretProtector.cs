using System;
using System.Security;
using mRemoteNG.Security.SymmetricEncryption;

namespace mRemoteNG.Security;

/// <summary>
/// Encrypts and decrypts the secrets mRemoteNG stores in its own settings — the default credential
/// password, the SQL Server password, the update proxy password.
/// </summary>
/// <remarks>
/// <para>
/// Reads follow a marker on the stored value; writes always use authenticated encryption. Values
/// written before this existed carry no marker and are decrypted with the legacy provider, so
/// nothing needs migrating and settings written by an older build keep working. They migrate when
/// they are next saved.
/// </para>
/// <para>
/// The legacy provider derives its key as an unsalted MD5 of the secret and encrypts with AES-CBC
/// and no authentication tag, so a stored value can be recovered cheaply and altered undetectably.
/// </para>
/// <para>
/// <b>The key is not changed here and must not become machine-bound.</b> These values travel with a
/// portable installation and are read during startup and during a connection attempt — points where
/// no recovery prompt belongs. That leaves them keyed on <c>Runtime.EncryptionKey</c>, which is the
/// legacy default unless the user set a master password: a real remaining weakness, and a smaller
/// one than an unsalted MD5.
/// </para>
/// </remarks>
public class SettingsSecretProtector
{
    /// <summary>
    /// Prefix identifying a value written with authenticated encryption.
    /// </summary>
    /// <remarks>
    /// Both providers emit base64, which never contains a colon, so a colon-terminated prefix
    /// cannot be mistaken for either format. Versioned so a future scheme can be told apart rather
    /// than guessed at.
    /// </remarks>
    private const string AeadMarker = "aead1:";

    /// <summary>How far into a value a marker separator may appear before it is one.</summary>
    private const int MarkerSearchLength = 16;

    private readonly ICryptographyProvider _aead;
    private readonly ICryptographyProvider _legacy;

    /// <summary>
    /// One instance for the application, so the AEAD provider's key cache is shared.
    /// </summary>
    /// <remarks>
    /// Deriving a PBKDF2 key at the configured iteration count costs hundreds of milliseconds, and
    /// these secrets are read on connection paths. A shared instance means the derivation happens
    /// once per key rather than once per read.
    /// </remarks>
    public static SettingsSecretProtector Default { get; } = new();

    public SettingsSecretProtector()
        : this(new AeadCryptographyProvider(), new LegacyRijndaelCryptographyProvider())
    {
    }

    public SettingsSecretProtector(ICryptographyProvider aead, ICryptographyProvider legacy)
    {
        ArgumentNullException.ThrowIfNull(aead);
        ArgumentNullException.ThrowIfNull(legacy);
        _aead = aead;
        _legacy = legacy;
    }

    /// <summary>Encrypts a settings secret. Always authenticated, always marked.</summary>
    public string Protect(string? plainText, SecureString key)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        return AeadMarker + _aead.Encrypt(plainText, key);
    }

    /// <summary>
    /// Decrypts a settings secret, choosing the provider from the value's marker.
    /// </summary>
    /// <exception cref="EncryptionException">
    /// The value carries a marker this build does not recognise. Falling back to the legacy provider
    /// would be worse than failing: it is unauthenticated, so it would return plausible bytes rather
    /// than an error, and the caller would hand a wrong password to a server.
    /// </exception>
    public string Unprotect(string? cipherText, SecureString key)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (cipherText.StartsWith(AeadMarker, StringComparison.Ordinal))
            return _aead.Decrypt(cipherText[AeadMarker.Length..], key);

        if (HasUnrecognisedMarker(cipherText))
            throw new EncryptionException(Resources.Language.Language.ErrorDecryptionFailed);

        // No marker: written before this existed, or provisioned through the registry, which is a
        // documented format an administrator produces with the password generator.
        return _legacy.Decrypt(cipherText, key);
    }

    /// <summary>Whether a value has been marked as something other than what this build writes.</summary>
    public static bool IsProtected(string? cipherText) =>
        cipherText?.StartsWith(AeadMarker, StringComparison.Ordinal) == true;

    private static bool HasUnrecognisedMarker(string cipherText)
    {
        int search = Math.Min(cipherText.Length, MarkerSearchLength);
        return cipherText.AsSpan(0, search).IndexOf(':') >= 0;
    }
}
