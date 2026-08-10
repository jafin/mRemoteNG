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
    private readonly Version _currentSupportedVersion = new(3, 5);

    private readonly IDatabaseConnector _databaseConnector;

    public SqlDatabaseVersionVerifier(IDatabaseConnector databaseConnector)
    {
        ArgumentNullException.ThrowIfNull(databaseConnector);
        _databaseConnector = databaseConnector;
    }

    public bool IsNewerThanSupported(Version dbVersion) =>
        dbVersion is not null && dbVersion.CompareTo(_currentSupportedVersion) > 0;

    public bool VerifyDatabaseVersion(Version dbVersion)
    {
        try
        {
            Version databaseVersion = dbVersion;

            if (databaseVersion.Equals(_currentSupportedVersion))
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

            // DB is at the highest current supported version
            if (databaseVersion.CompareTo(_currentSupportedVersion) == 0)
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