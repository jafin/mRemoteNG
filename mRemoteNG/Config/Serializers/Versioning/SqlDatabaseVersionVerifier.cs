using System;
using System.Globalization;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Config.Serializers.Versioning;

[SupportedOSPlatform("windows")]
public class SqlDatabaseVersionVerifier : ISqlDatabaseVersionVerifier
{
    /// <summary>
    /// The schema every existing database is at. Secrets at this version are legacy-encrypted.
    /// </summary>
    /// <remarks>
    /// <b>This is not the version a new database is created at</b>, and §2 of this change recorded
    /// that it was. The reasoning then was the connection file's: write the older format so upstream
    /// mRemoteNG can still read it, and let the stronger one be chosen deliberately. §3 makes that
    /// untenable — the saver refuses to write a legacy database, so creating one here would produce
    /// a database this build could read and never write to again, broken on its second save by its
    /// own creator. New databases are created at
    /// <see cref="Security.Factories.CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion"/>.
    /// What remains true is that an <i>existing</i> database is never upgraded except deliberately.
    /// </remarks>
    public static readonly Version SchemaVersion = new(3, 5);

    /// <summary>The highest version this build can read. Above this a database is refused.</summary>
    public static readonly Version HighestSupportedVersion =
        Security.Factories.CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion;

    private readonly Version _currentSupportedVersion = HighestSupportedVersion;

    /// <summary>
    /// The oldest version this build reads as it stands, without upgrading it.
    /// </summary>
    /// <remarks>
    /// 3.5 and 3.6 share a schema exactly and differ only in how the secret columns are encrypted,
    /// so both are readable and the version says which provider to read them with. This is why the
    /// check below is a range rather than an equality: a 3.5 database is not out of date in the
    /// sense the upgrader chain understands — there is nothing to upgrade — and running it through
    /// the chain would find no upgrader and report a database that works perfectly as unsupported.
    /// What a 3.5 database cannot do is accept a save; that refusal belongs to the saver, which is
    /// the only place that knows a write is being attempted.
    /// </remarks>
    private readonly Version _oldestReadableVersion = SchemaVersion;

    private readonly IDatabaseConnector _databaseConnector;

    public SqlDatabaseVersionVerifier(IDatabaseConnector databaseConnector)
    {
        ArgumentNullException.ThrowIfNull(databaseConnector);
        _databaseConnector = databaseConnector;
    }

    public bool IsNewerThanSupported(Version dbVersion) =>
        dbVersion is not null && dbVersion.CompareTo(_currentSupportedVersion) > 0;

    /// <summary>Whether this build reads the database at this version without changing it first.</summary>
    private bool IsReadableAsItStands(Version databaseVersion) =>
        databaseVersion.CompareTo(_oldestReadableVersion) >= 0 &&
        databaseVersion.CompareTo(_currentSupportedVersion) <= 0;

    public bool VerifyDatabaseVersion(Version dbVersion)
    {
        try
        {
            Version databaseVersion = dbVersion;

            if (IsReadableAsItStands(databaseVersion))
            {
                return true;
            }

            // Reported before the upgraders are consulted, and separately from the generic
            // incompatibility warning below. None of them can downgrade a database, so running
            // them would be pointless, and the two cases need opposite handling by the caller —
            // an old database may be attempted, a newer one may not.
            if (IsNewerThanSupported(databaseVersion))
            {
                Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg,
                    string.Format(CultureInfo.InvariantCulture, Language.ErrorDatabaseVersionNewerThanClient,
                        databaseVersion, GeneralAppInfo.ProductName, _currentSupportedVersion));
                return false;
            }

            IVersionUpgrader[] dbUpgraders = new IVersionUpgrader[]
            {
                new SqlVersion22To23Upgrader(_databaseConnector),
                new SqlVersion23To24Upgrader(_databaseConnector),
                new SqlVersion24To25Upgrader(_databaseConnector),
                new SqlVersion25To26Upgrader(_databaseConnector),
                new SqlVersion26To27Upgrader(_databaseConnector),
                new SqlVersion27To28Upgrader(_databaseConnector),
                new SqlVersion28To29Upgrader(_databaseConnector),
                new SqlVersion29To30Upgrader(_databaseConnector),
                new SqlVersion30To31Upgrader(_databaseConnector),
                new SqlVersion31To32Upgrader(_databaseConnector),
                new SqlVersion32To33Upgrader(_databaseConnector),
                new SqlVersion33To34Upgrader(_databaseConnector),
                new SqlVersion34To35Upgrader(_databaseConnector),
            };

            foreach (IVersionUpgrader upgrader in dbUpgraders)
            {
                if (upgrader.CanUpgrade(databaseVersion))
                {
                    databaseVersion = upgrader.Upgrade();
                }
            }

            // The upgraders end at the oldest readable version, so this is the same range check as
            // above rather than an equality with the highest one — an upgraded database lands at
            // 3.5, and 3.5 is readable.
            if (IsReadableAsItStands(databaseVersion))
            {
                return true;
            }

            Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, string.Format(CultureInfo.InvariantCulture, Language.ErrorBadDatabaseVersion, databaseVersion, GeneralAppInfo.ProductName));
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, string.Format(CultureInfo.InvariantCulture, Language.ErrorVerifyDatabaseVersionFailed, ex.Message));
        }

        return false;
    }
}