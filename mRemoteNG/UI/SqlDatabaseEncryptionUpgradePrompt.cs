using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;

namespace mRemoteNG.UI;

/// <summary>
/// Asks whether to re-encrypt a SQL database's secrets, and does it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reached from the SQL options page and from nowhere else.</b> Nothing offers this when a
/// database is opened, and that is deliberate rather than unfinished — see design.md. The connection
/// file can offer its own hardening on open because the person prompted is the person affected; a
/// database is shared, so the same offer would let whoever happens to start the application first
/// decide for everyone. Somebody who goes looking in the options page for the database they
/// administer is a much better guess at the person entitled to decide.
/// </para>
/// <para>
/// The confirmation is most of what this class is. Upgrading cannot be undone from inside mRemoteNG
/// and locks out every client that has not taken this change — so what it costs is said before
/// anything is touched, in the order somebody needs in order to answer: what it fixes, who it breaks,
/// and that a backup is the only way back.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SqlDatabaseEncryptionUpgradePrompt
{
    /// <summary>
    /// The database to work on. A seam, so the flow can be tested without a server: everything else
    /// here would otherwise need one just to find out that the user said no.
    /// </summary>
    internal static Func<IDatabaseConnector> Connector { get; set; } =
        DatabaseConnectorFactory.DatabaseConnectorFromSettings;

    internal static ISqlDatabaseMetaDataRetriever MetaDataRetriever { get; set; } =
        new SqlDatabaseMetaDataRetriever();

    /// <summary>
    /// Collects the existing master password. Not verified by re-entry — unlike a password being
    /// set, this one is being recalled, and a second box only adds a way to mistype it twice.
    /// </summary>
    internal static Func<string, Optional<SecureString>> PasswordPrompt { get; set; } =
        name => MiscTools.PasswordDialog(name, verify: false);

    /// <summary>
    /// Collects a master password for a database that has none. Verified by re-entry, because this
    /// one is being chosen rather than recalled and there is nothing to check it against later — a
    /// mistyped password here is a database nobody can open.
    /// </summary>
    internal static Func<string, Optional<SecureString>> NewPasswordPrompt { get; set; } =
        name => MiscTools.PasswordDialog(name, verify: true);

    /// <summary>Shown so tests can assert what was said rather than only what was done.</summary>
    internal static Action<Control?, string, string> ShowMessage { get; set; } =
        (owner, text, title) => MessageBox.Show(owner, text, title, MessageBoxButtons.OK,
                                                MessageBoxIcon.Information);

    /// <summary>Asked before anything is changed. True upgrades.</summary>
    internal static Func<Control?, string, bool> Confirm { get; set; } =
        (owner, text) => MessageBox.Show(owner, text, Language.SqlUpgradeTitle, MessageBoxButtons.YesNo,
                                         MessageBoxIcon.Warning) == DialogResult.Yes;

    public static void Ask(Control? owner)
    {
        try
        {
            using IDatabaseConnector connector = Connector();

            if (!connector.IsConnected)
                connector.Connect();

            SqlConnectionListMetaData? metaData = MetaDataRetriever.GetDatabaseMetaData(connector);

            if (metaData is null)
            {
                ShowMessage(owner, Language.SqlUpgradeNoDatabase, Language.SqlUpgradeTitle);
                return;
            }

            if (!SqlDatabaseEncryptionUpgrade.IsAvailableFor(metaData))
            {
                ShowMessage(owner, Language.SqlUpgradeNotNeeded, Language.SqlUpgradeTitle);
                return;
            }

            // Whether this database already has a master password decides both what the warning has
            // to say and what has to be collected, so it is settled before anything is shown.
            bool hasMasterPassword = SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData);

            if (!Confirm(owner, BuildExplanation(settingMasterPassword: !hasMasterPassword)))
            {
                ShowMessage(owner, Language.SqlUpgradeDeclined, Language.SqlUpgradeTitle);
                return;
            }

            Upgrade(owner, connector, metaData, hasMasterPassword);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage(
                "Upgrading the SQL database's encryption failed", ex);
            ShowMessage(owner, Language.SqlUpgradeFailed, Language.SqlUpgradeTitle);
        }
    }

    /// <summary>
    /// Collects what is needed and performs the upgrade.
    /// </summary>
    /// <param name="hasMasterPassword">
    /// Whether the database already has one. When it does, that password is recalled and kept; when
    /// it does not, one is chosen now — the upgrade sets a master password rather than carrying the
    /// built-in default key forward under a stronger cipher.
    /// </param>
    private static void Upgrade(Control? owner, IDatabaseConnector connector,
                                SqlConnectionListMetaData metaData, bool hasMasterPassword)
    {
        // Empty for a database with no master password: Apply authenticates whatever it is given
        // against the sentinel, and for an unprotected store that check passes on the built-in
        // default key and ignores this entirely.
        SecureString existingPassword = new();
        SecureString? newMasterPassword = null;

        try
        {
            if (hasMasterPassword)
            {
                Optional<SecureString> supplied = PasswordPrompt(Language.SqlUpgradeMasterPasswordName);

                if (!supplied.Any() || supplied.First() is not { Length: > 0 } typed)
                {
                    // Declining the password is declining the upgrade, and nothing has been touched
                    // yet — this is still the cheap way out.
                    ShowMessage(owner, Language.SqlUpgradeDeclined, Language.SqlUpgradeTitle);
                    return;
                }

                existingPassword.Dispose();
                existingPassword = typed;
                newMasterPassword = typed;
            }
            else
            {
                Optional<SecureString> chosen = NewPasswordPrompt(Language.SqlUpgradeSetPasswordName);

                if (!chosen.Any() || chosen.First() is not { Length: > 0 } picked)
                {
                    // Its own message rather than the generic decline: refusing here is refusing the
                    // password, and somebody who expected the upgrade to proceed without one needs
                    // to know that it cannot.
                    ShowMessage(owner, Language.SqlUpgradePasswordNotSet, Language.SqlUpgradeTitle);
                    return;
                }

                newMasterPassword = picked;
            }

            int rewritten = SqlDatabaseEncryptionUpgrade.Apply(connector, existingPassword,
                                                               newMasterPassword, MetaDataRetriever);

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"The SQL database was upgraded to authenticated encryption. {rewritten} connections " +
                "were re-encrypted. It now requires its master password, and clients that have not " +
                "taken this change can no longer open it.",
                true);

            ShowMessage(owner,
                string.Format(CultureInfo.CurrentCulture, Language.SqlUpgradeDone, rewritten),
                Language.SqlUpgradeTitle);
        }
        catch (EncryptionException)
        {
            // Its own message, because it is the one failure the user can act on. Apply refuses
            // before touching a row, so "nothing was changed" is a fact and not a hope.
            ShowMessage(owner, Language.SqlUpgradeWrongPassword, Language.SqlUpgradeTitle);
        }
        finally
        {
            existingPassword.Dispose();

            // Only when it is a different object. For a database that already had one, both names
            // refer to the password just disposed.
            if (!ReferenceEquals(newMasterPassword, existingPassword))
                newMasterPassword?.Dispose();
        }
    }

    /// <summary>
    /// What state the configured database is in, for the options page's status line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Describes the <i>saved</i> database, the same one <see cref="Ask"/> would act on. The options
    /// page's test-connection button works on whatever is currently typed into it, which may be
    /// another server entirely — a status line about one database beside a button that upgrades
    /// another is how an irreversible change lands on the wrong one.
    /// </para>
    /// <para>
    /// Called only after a connection has already been established, because reading the metadata of
    /// an empty database creates the schema in it — a reasonable thing for a load to do and a rude
    /// one to do to a database name somebody is still typing.
    /// </para>
    /// </remarks>
    public static (bool UpgradeAvailable, string Status) ReadStatus()
    {
        try
        {
            using IDatabaseConnector connector = Connector();

            if (!connector.IsConnected)
                connector.Connect();

            SqlConnectionListMetaData? metaData = MetaDataRetriever.GetDatabaseMetaData(connector);

            if (metaData is null)
                return (false, Language.SqlUpgradeStatusNoDatabase);

            if (!SqlDatabaseEncryptionUpgrade.IsAvailableFor(metaData))
                return (false, Language.SqlUpgradeStatusCurrent);

            // Two different states, and the weaker one is the one that reads as fine. A database
            // with no master password is encrypted under a constant published in this application's
            // source, so "old, weak encryption" understates it to the point of being misleading:
            // there is nothing to break. An administrator who does not already know what that key
            // is will not act on a sentence about cipher strength, so this says what it means for
            // the passwords instead.
            return (true, SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData)
                ? Language.SqlUpgradeStatusLegacy
                : Language.SqlUpgradeStatusDefaultKey);
        }
        catch (Exception ex)
        {
            // A status line is not worth a dialog, but it must not go blank either. An empty line is
            // indistinguishable from "not looked yet", so a failure here used to present as the
            // feature simply not working, with the reason available only to somebody who thought to
            // open the notifications panel. Say that the read failed, and point at the panel for why.
            Runtime.MessageCollector.AddExceptionMessage(
                "Reading the SQL database's encryption state failed", ex, MessageClass.WarningMsg);
            return (false, Language.SqlUpgradeStatusUnknown);
        }
    }

    /// <summary>
    /// The confirmation text. Assembled here rather than at the dialog so a test can assert what a
    /// security warning says without needing a window.
    /// </summary>
    /// <param name="settingMasterPassword">
    /// Adds what setting a master password costs: everyone who uses the database needs it, the
    /// administrator has to hand it out, and losing it loses the connections. Said here rather than
    /// at the password box, because by then the decision has been taken and the box is the wrong
    /// place to discover that colleagues are about to be locked out.
    /// </param>
    internal static string BuildExplanation(bool settingMasterPassword) =>
        string.Join(Environment.NewLine + Environment.NewLine,
            settingMasterPassword
                ? new[]
                {
                    Language.SqlUpgradeInstruction, Language.SqlUpgradeWhat, Language.SqlUpgradeDistribute,
                    Language.SqlUpgradeClients, Language.SqlUpgradeIrreversible
                }
                : [
                    Language.SqlUpgradeInstruction, Language.SqlUpgradeWhat, Language.SqlUpgradeClients,
                    Language.SqlUpgradeIrreversible
                ]);
}
