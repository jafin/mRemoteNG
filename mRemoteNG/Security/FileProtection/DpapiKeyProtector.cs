using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace mRemoteNG.Security.FileProtection;

/// <summary>
/// Wraps the file key with DPAPI, bound to the Windows account that wrote the file.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="DataProtectionScope.CurrentUser"/>, not <see cref="DataProtectionScope.LocalMachine"/>.</b>
/// <c>LocalMachine</c> would survive a profile rebuild and keep multi-user machines working, and it is
/// the wrong answer: any account on the box could then decrypt the file, including a service account
/// an attacker already has. The threat this change addresses is a connection file leaving the
/// machine, and <c>CurrentUser</c> is what binds it. The recovery-password protector covers the cases
/// <c>LocalMachine</c> would have covered, without the exposure.
/// </para>
/// <para>
/// <b>The scope binds what this writes and cannot be checked on read.</b> It is carried inside the
/// blob, and Win32's <c>CryptUnprotectData</c> takes no scope argument, so the value passed to
/// <see cref="ProtectedData.Unprotect"/> is inert — a <c>LocalMachine</c> blob unwraps perfectly
/// through a <c>CurrentUser</c> call. Do not add a check that appears to enforce the scope on the
/// read side; there is nothing to enforce it against, and the appearance is worse than the absence.
/// </para>
/// <para>
/// This is the protector that is absent in the portable edition, which is why it is a separate type
/// from the one that always exists.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class DpapiKeyProtector
{
    /// <summary>
    /// Additional entropy mixed into the DPAPI key.
    /// </summary>
    /// <remarks>
    /// Not a secret and not pretending to be one — it is published here. It exists for domain
    /// separation: a blob this application produced cannot be unprotected by another program running
    /// as the same user that merely calls <c>Unprotect</c> with no entropy.
    /// </remarks>
    private static readonly byte[] Entropy = "mRemoteNG.ConnectionFileKey.v1"u8.ToArray();

    public static string Wrap(ConnectionFileKey fileKey)
    {
        ArgumentNullException.ThrowIfNull(fileKey);

        byte[] plaintext = fileKey.Bytes.ToArray();
        try
        {
            return Convert.ToBase64String(
                ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <exception cref="KeyProtectionException">
    /// The blob is unreadable, or belongs to another Windows account or another machine. Never
    /// returns a wrong key: DPAPI authenticates, so failure is an exception rather than plausible
    /// bytes.
    /// </exception>
    public static ConnectionFileKey Unwrap(string? wrappedKey)
    {
        if (string.IsNullOrWhiteSpace(wrappedKey))
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                "This connection file carries no machine protector.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(wrappedKey);
        }
        catch (FormatException ex)
        {
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                "The machine protector on this connection file is not readable.", ex);
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return ConnectionFileKey.FromBytes(plaintext);
        }
        catch (CryptographicException ex)
        {
            // Retryable in the sense that a different account could open it — which is why the
            // caller falls through to the recovery password rather than stopping here.
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.WrongSecret,
                "This connection file was protected by a different Windows account or machine.", ex);
        }
        catch (ArgumentException ex)
        {
            // Unprotected to something that is not a file key. Reported as a protector failure
            // rather than allowed to surface as an argument fault from deep in the load path.
            throw new KeyProtectionException(KeyProtector.Machine, KeyProtectionFailure.Unusable,
                "The machine protector on this connection file did not yield a file key.", ex);
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
