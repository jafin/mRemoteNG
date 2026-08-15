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

            if (!Confirm(owner, BuildExplanation()))
            {
                ShowMessage(owner, Language.SqlUpgradeDeclined, Language.SqlUpgradeTitle);
                return;
            }

            Upgrade(owner, connector, metaData);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage(
                "Upgrading the SQL database's encryption failed", ex);
            ShowMessage(owner, Language.SqlUpgradeFailed, Language.SqlUpgradeTitle);
        }
    }

    private static void Upgrade(Control? owner, IDatabaseConnector connector,
                                SqlConnectionListMetaData metaData)
    {
        // Empty rather than null when the database has no master password: Apply authenticates
        // whatever it is given against the sentinel, and for an unprotected store that check passes
        // on the built-in default key and ignores this entirely.
        SecureString masterPassword = new();

        try
        {
            if (SqlDatabaseEncryptionUpgrade.RequiresMasterPassword(metaData))
            {
                Optional<SecureString> supplied = PasswordPrompt(Language.SqlUpgradeMasterPasswordName);

                if (!supplied.Any() || supplied.First() is not { Length: > 0 } typed)
                {
                    // Declining the password is declining the upgrade, and nothing has been touched
                    // yet — this is still the cheap way out.
                    ShowMessage(owner, Language.SqlUpgradeDeclined, Language.SqlUpgradeTitle);
                    return;
                }

                masterPassword.Dispose();
                masterPassword = typed;
            }

            int rewritten = SqlDatabaseEncryptionUpgrade.Apply(connector, masterPassword, MetaDataRetriever);

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"The SQL database was upgraded to authenticated encryption. {rewritten} connections " +
                "were re-encrypted. Clients that have not taken this change can no longer open it.",
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
            masterPassword.Dispose();
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

            return SqlDatabaseEncryptionUpgrade.IsAvailableFor(metaData)
                ? (true, Language.SqlUpgradeStatusLegacy)
                : (false, Language.SqlUpgradeStatusCurrent);
        }
        catch (Exception ex)
        {
            // A status line is not worth a dialog. The message channel keeps the reason findable.
            Runtime.MessageCollector.AddExceptionMessage(
                "Reading the SQL database's encryption state failed", ex, MessageClass.WarningMsg);
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// The confirmation text. Assembled here rather than at the dialog so a test can assert what a
    /// security warning says without needing a window.
    /// </summary>
    internal static string BuildExplanation() =>
        string.Join(Environment.NewLine + Environment.NewLine,
            Language.SqlUpgradeInstruction, Language.SqlUpgradeWhat, Language.SqlUpgradeClients,
            Language.SqlUpgradeIrreversible);
}
