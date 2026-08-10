using System;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Security;

/// <summary>Which kind of store is being asked about. The warning differs; the decision does not.</summary>
public enum StorageFormatStoreKind
{
    ConnectionFile,
    SqlDatabase
}

/// <summary>What the user chose when asked to raise a store's level.</summary>
public enum StorageFormatUpgradeChoice
{
    /// <summary>Nothing happens. The store stays byte-compatible with what upstream reads.</summary>
    Decline,

    /// <summary>Raise the level. The next save writes the hardened format.</summary>
    Harden,

    /// <summary>Take the classic copy first. The level is not raised by choosing this.</summary>
    ExportClassicCopy
}

/// <summary>
/// The text of the upgrade confirmation, assembled without reference to any dialog so that what it
/// says can be tested. What shows it is <see cref="UI.StorageFormatUpgradePrompt"/>.
/// </summary>
/// <param name="Instruction">The question, as one line.</param>
/// <param name="Content">What the user gives up, in terms of applications.</param>
/// <param name="ExpandedInfo">The cryptographic detail, for those who go looking for it.</param>
public sealed record StorageFormatUpgradeMessage(string Instruction, string Content, string ExpandedInfo);

/// <summary>
/// The single confirmation for raising a store to the hardened format, and the act of raising it.
/// </summary>
/// <remarks>
/// <para>
/// One confirmation for all hardening rather than one per change. A user cannot meaningfully consent
/// to four separate cryptographic changes, and asking four times would train them to click through
/// the fourth without reading it. The level is the unit they decide about.
/// </para>
/// <para>
/// Nothing here shows a dialog. The message is assembled by <see cref="BuildMessage"/> and the level
/// raised by <see cref="Apply"/>, so both can be tested without a window — which is the only way to
/// have a test assert what a security warning actually says.
/// </para>
/// </remarks>
public static class StorageFormatUpgrade
{
    /// <summary>
    /// Builds what the user is asked, for a store of this kind.
    /// </summary>
    /// <remarks>
    /// Applications first, cryptography last. "This store will use PBKDF2-HMAC-SHA256" tells a user
    /// nothing about what they are giving up; "upstream mRemoteNG will no longer open this file" is
    /// the sentence they can decide on. The detail is not hidden — it is placed after the part that
    /// matters, for the people who want it.
    /// </remarks>
    public static StorageFormatUpgradeMessage BuildMessage(StorageFormatStoreKind storeKind)
    {
        string content = Language.StorageFormatUpgradeApplications +
                         Environment.NewLine + Environment.NewLine +
                         Language.StorageFormatUpgradeBackups;

        // The person confirming a shared store is not the only person affected by it, and unlike the
        // connection file they cannot undo it for the others by exporting a copy.
        if (storeKind == StorageFormatStoreKind.SqlDatabase)
            content += Environment.NewLine + Environment.NewLine + Language.StorageFormatUpgradeSqlClients;

        return new StorageFormatUpgradeMessage(
            storeKind == StorageFormatStoreKind.SqlDatabase
                ? Language.StorageFormatUpgradeInstructionSql
                : Language.StorageFormatUpgradeInstructionFile,
            content,
            Language.StorageFormatUpgradeCryptography);
    }

    /// <summary>
    /// The confirmation's command buttons, in the order <see cref="StorageFormatUpgradeChoice"/>
    /// reads them back, as the pipe-delimited list the task dialog expects.
    /// </summary>
    /// <remarks>
    /// Built here rather than at the dialog so a test can hold the delimiter to account. A pipe
    /// inside any one label silently becomes an extra button, and the choice the user's click maps
    /// to shifts by one — which on this dialog means a click meant for the export hardening the
    /// store instead.
    /// </remarks>
    public static string CommandButtons() =>
        string.Join(ButtonDelimiter,
            Language.StorageFormatUpgradeHarden,
            Language.StorageFormatUpgradeExportFirst,
            Language._Cancel);

    internal const string ButtonDelimiter = "|";

    /// <summary>How many buttons <see cref="CommandButtons"/> is meant to produce.</summary>
    internal const int ExpectedButtonCount = 3;

    /// <summary>
    /// Applies a choice to a store's root node, and reports whether the level changed.
    /// </summary>
    /// <remarks>
    /// Only <see cref="StorageFormatUpgradeChoice.Harden"/> raises anything. Taking the classic copy
    /// is explicitly not consent to harden: a user who asked for the way back before deciding has
    /// not decided yet, and treating the export as agreement would harden the store behind them.
    /// </remarks>
    public static bool Apply(RootNodeInfo rootNode, StorageFormatUpgradeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(rootNode);

        if (choice != StorageFormatUpgradeChoice.Harden)
            return false;

        if (rootNode.StorageFormat == StorageFormatLevel.Hardened)
            return false;

        rootNode.StorageFormat = StorageFormatLevel.Hardened;
        return true;
    }
}
