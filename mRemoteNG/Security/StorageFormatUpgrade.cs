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
    /// What the user is told before being asked for a recovery password.
    /// </summary>
    /// <remarks>
    /// Here rather than at the dialog for the same reason <see cref="BuildMessage"/> is: a test can
    /// only assert what a security warning says if assembling it does not need a window.
    /// <para>
    /// The shared-store paragraph is said <i>before</i> the password is asked for, never after. A
    /// user who learns only afterwards that everyone sharing this file will need the password has
    /// already chosen one on the assumption that they alone would use it.
    /// </para>
    /// </remarks>
    /// <param name="willWriteMachineProtector">
    /// From <see cref="FileProtection.MachineProtectorPolicy.ShouldWriteMachineProtector"/>.
    /// </param>
    /// <param name="isPortableEdition">
    /// Kept apart from the parameter above because they suppress the machine protector for different
    /// reasons and only one of them is about sharing. Telling a portable user their file might be
    /// shared with colleagues would be a guess presented as a fact.
    /// </param>
    public static string BuildRecoveryPasswordExplanation(bool willWriteMachineProtector, bool isPortableEdition)
    {
        string explanation = Language.RecoveryPasswordWhy;

        if (!willWriteMachineProtector && !isPortableEdition)
            explanation += Environment.NewLine + Environment.NewLine + Language.RecoveryPasswordSharedStore;

        return explanation;
    }

    /// <summary>
    /// What the user is told after declining, whether they declined the format or only the recovery
    /// password that hardening a connection file requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declining is a legitimate answer and this does not argue with it. What it refuses to do is
    /// leave the user believing the file they kept is protected. Until this change the application
    /// said nothing at all here, and silence after a security question reads as reassurance.
    /// </para>
    /// <para>
    /// The sentence about the published key is only said when it is <i>true</i>, which is why
    /// <paramref name="storeIsKeyedOnThePublishedDefault"/> exists rather than being assumed from the
    /// store still being classic. A classic store with a master password is encrypted under that
    /// password; telling its owner their key is published in our source would be false, and a warning
    /// that turns out to be false is worth less than no warning.
    /// </para>
    /// <para>
    /// It is not gated on the portable edition either, though task 7.2 is where it was written. The
    /// key is equally published for both editions, and the difference is only that portable has no
    /// machine protector to offer as the easy answer — so a portable user is likelier to arrive here.
    /// Saying it to installed users too costs nothing and is the same truth.
    /// </para>
    /// </remarks>
    /// <param name="recoveryPasswordDeclined">
    /// <see langword="true"/> when the format was accepted and the recovery password was not, which
    /// leaves the store exactly as classic as an outright refusal but for a reason the user should
    /// hear back, since they may have thought they had hardened it.
    /// </param>
    public static string BuildDeclineExplanation(bool recoveryPasswordDeclined,
                                                 bool storeIsKeyedOnThePublishedDefault)
    {
        string explanation = recoveryPasswordDeclined
            ? Language.RecoveryPasswordDeclined
            : Language.StorageFormatDeclined;

        if (storeIsKeyedOnThePublishedDefault)
            explanation += Environment.NewLine + Environment.NewLine + Language.StorageFormatDeclinedLegacyKey;

        return explanation;
    }

    /// <summary>
    /// Whether this store is still encrypted under <c>ConnectionFileDefaults.LegacyEncryptionKey</c>
    /// — the constant this whole change exists to stop writing.
    /// </summary>
    /// <remarks>
    /// The same comparison <c>XmlRootNodeSerializer</c> makes to choose the sentinel, so what the
    /// user is told and what the writer does cannot disagree. A store holding key protectors is
    /// excluded outright: its contents are keyed on its own random key, and
    /// <see cref="RootNodeInfo.PasswordString"/> is not what opens it.
    /// </remarks>
    public static bool StoreIsKeyedOnThePublishedDefault(RootNodeInfo rootNode) =>
        rootNode.KeyProtection is null &&
        string.Equals(rootNode.PasswordString, rootNode.DefaultPassword, StringComparison.Ordinal);

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

    /// <summary>
    /// Whether the confirmation left something that has to be written to the store.
    /// </summary>
    /// <remarks>
    /// Raising the level is not the only thing that can change, and <see cref="Apply"/> only reports
    /// on the level — which is all it applies. A connection file already at the hardened level that
    /// has just been given a random key and two protectors has changed a great deal and gets
    /// <see langword="false"/> from <see cref="Apply"/>. Deciding from that alone leaves the
    /// protectors in memory, never written, on precisely the stores that most needed them: the ones
    /// hardened before per-file keys existed, still encrypted under the published default constant.
    /// </remarks>
    /// <param name="wasAlreadyProtected">
    /// Read <em>before</em> protection is established, since establishing it is what changes the
    /// answer.
    /// </param>
    public static bool ConfirmationChangedTheStore(bool levelWasRaised, bool wasAlreadyProtected) =>
        levelWasRaised || !wasAlreadyProtected;
}
