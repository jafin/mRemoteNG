using System;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Messages;

namespace mRemoteNG.Config.Serializers.Versioning;

[SupportedOSPlatform("windows")]
public class SqlVersion32To33Upgrader(IDatabaseConnector databaseConnector) : IVersionUpgrader
{
    private readonly Version _version = new(3, 3);
    private readonly IDatabaseConnector _databaseConnector = databaseConnector ?? throw new ArgumentNullException(nameof(databaseConnector));

    public bool CanUpgrade(Version currentVersion)
    {
        return currentVersion == new Version(3, 2) ||
               (currentVersion <= new Version(3, 3) &&
                currentVersion < _version);
    }

    public Version Upgrade()
    {
        Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
            $"Upgrading database to version {_version}.");

        const string msSqlAlter = @"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='Hidden')
    ALTER TABLE tblExternalTools ADD [Hidden] [bit] NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='AuthType')
    ALTER TABLE tblExternalTools ADD [AuthType] [nvarchar](256) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='AuthUsername')
    ALTER TABLE tblExternalTools ADD [AuthUsername] [nvarchar](512) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='AuthPassword')
    ALTER TABLE tblExternalTools ADD [AuthPassword] [nvarchar](1024) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='PrivateKeyFile')
    ALTER TABLE tblExternalTools ADD [PrivateKeyFile] [nvarchar](1024) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblExternalTools' AND COLUMN_NAME='Passphrase')
    ALTER TABLE tblExternalTools ADD [Passphrase] [nvarchar](1024) NOT NULL DEFAULT '';
";

        // MySQL: individual ALTERs (no IF NOT EXISTS for ADD COLUMN in MySQL 8.x)
        string[] mySqlAlters =
        [
            "ALTER TABLE `tblExternalTools` ADD COLUMN `Hidden` tinyint NOT NULL DEFAULT 0",
            "ALTER TABLE `tblExternalTools` ADD COLUMN `AuthType` varchar(256) NOT NULL DEFAULT ''",
            "ALTER TABLE `tblExternalTools` ADD COLUMN `AuthUsername` varchar(512) NOT NULL DEFAULT ''",
            "ALTER TABLE `tblExternalTools` ADD COLUMN `AuthPassword` varchar(1024) NOT NULL DEFAULT ''",
            "ALTER TABLE `tblExternalTools` ADD COLUMN `PrivateKeyFile` varchar(1024) NOT NULL DEFAULT ''",
            "ALTER TABLE `tblExternalTools` ADD COLUMN `Passphrase` varchar(1024) NOT NULL DEFAULT ''",
        ];

        SqlMigrationHelper.ExecuteMigrationIdempotent(_databaseConnector, _version, msSqlAlter, mySqlAlters);
        return _version;
    }
}