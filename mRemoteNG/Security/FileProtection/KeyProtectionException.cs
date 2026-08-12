using System;

namespace mRemoteNG.Security.FileProtection;

/// <summary>Which protector was asked for the file key.</summary>
public enum KeyProtector
{
    /// <summary>DPAPI at <c>CurrentUser</c> scope — the Windows account that wrote the file.</summary>
    Machine,

    /// <summary>The recovery password, which is what travels with a copy of the file.</summary>
    RecoveryPassword
}

/// <summary>Why a protector did not produce the file key.</summary>
/// <remarks>
/// The distinction that matters is whether trying again could ever work. Asking for a recovery
/// password three times against a protector that is structurally unreadable teaches the user their
/// password is wrong when it is not, which is the same misdiagnosis the storage-format refusal exists
/// to avoid.
/// </remarks>
public enum KeyProtectionFailure
{
    /// <summary>
    /// The protector is intact but this secret did not open it — a wrong recovery password, or a
    /// DPAPI blob belonging to another account. A different secret, or a different account, could.
    /// </summary>
    WrongSecret,

    /// <summary>
    /// The protector cannot be used at all: absent, truncated, not base64, or recording a format or
    /// parameters this build does not accept. No secret opens it.
    /// </summary>
    Unusable,

    /// <summary>
    /// Nothing was wrong with the protector; no secret was offered. The user cancelled, or the caller
    /// had no way to ask.
    /// </summary>
    NotSupplied
}

/// <summary>
/// A protector could not produce the file key.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from a parse failure on purpose. <c>XmlConnectionsLoader.TryRecoverFromBackup</c> exists
/// for a corrupt file and walks the whole backup set looking for one that parses; a protector failure
/// is not corruption — the backups are fine and every one of them will fail identically, so iterating
/// produces one misleading warning per backup and buries the cause. See design.md, and
/// <c>replace-default-connection-file-key</c> task 6.2.
/// </para>
/// <para>
/// <see cref="Protector"/> says which one failed, because the two mean different things to the user:
/// the machine protector failing says the file came from elsewhere, and the recovery password
/// failing says the password was wrong. <see cref="Failure"/> says whether asking again is worth
/// anything.
/// </para>
/// </remarks>
[Serializable]
public class KeyProtectionException : Exception
{
    public KeyProtector Protector { get; }

    public KeyProtectionFailure Failure { get; }

    /// <summary>Whether a different secret could open this protector.</summary>
    public bool IsRetryable => Failure == KeyProtectionFailure.WrongSecret;

    public KeyProtectionException(KeyProtector protector, KeyProtectionFailure failure, string message)
        : base(message)
    {
        Protector = protector;
        Failure = failure;
    }

    public KeyProtectionException(KeyProtector protector, KeyProtectionFailure failure, string message,
                                  Exception innerException)
        : base(message, innerException)
    {
        Protector = protector;
        Failure = failure;
    }
}
