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
/// failing says the password was wrong.
/// </para>
/// </remarks>
[Serializable]
public class KeyProtectionException : Exception
{
    public KeyProtector Protector { get; }

    public KeyProtectionException(KeyProtector protector, string message)
        : base(message) => Protector = protector;

    public KeyProtectionException(KeyProtector protector, string message, Exception innerException)
        : base(message, innerException) => Protector = protector;
}
