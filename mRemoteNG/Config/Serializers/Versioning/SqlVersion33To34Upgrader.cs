using System;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Messages;

namespace mRemoteNG.Config.Serializers.Versioning;

[SupportedOSPlatform("windows")]
public class SqlVersion33To34Upgrader(IDatabaseConnector databaseConnector) : IVersionUpgrader
{
    private readonly Version _version = new(3, 4);
    private readonly IDatabaseConnector _databaseConnector = databaseConnector ?? throw new ArgumentNullException(nameof(databaseConnector));

    public bool CanUpgrade(Version currentVersion)
    {
        return currentVersion == new Version(3, 3) ||
               (currentVersion <= new Version(3, 4) &&
                currentVersion < _version);
    }

    public Version Upgrade()
    {
        Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
            $"Upgrading database to version {_version}.");

        const string msSqlAlter = @"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblCons' AND COLUMN_NAME='RedirectWebAuthn')
    ALTER TABLE tblCons ADD [RedirectWebAuthn] [bit] NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblCons' AND COLUMN_NAME='EnableRdsAadAuth')
    ALTER TABLE tblCons ADD [EnableRdsAadAuth] [bit] NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblCons' AND COLUMN_NAME='InheritRedirectWebAuthn')
    ALTER TABLE tblCons ADD [InheritRedirectWebAuthn] [bit] NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='tblCons' AND COLUMN_NAME='InheritEnableRdsAadAuth')
    ALTER TABLE tblCons ADD [InheritEnableRdsAadAuth] [bit] NOT NULL DEFAULT 0;
";

        string[] mySqlAlters =
        [
            "ALTER TABLE `tblCons` ADD COLUMN `RedirectWebAuthn` tinyint NOT NULL DEFAULT 0",
            "ALTER TABLE `tblCons` ADD COLUMN `EnableRdsAadAuth` tinyint NOT NULL DEFAULT 0",
            "ALTER TABLE `tblCons` ADD COLUMN `InheritRedirectWebAuthn` tinyint NOT NULL DEFAULT 0",
            "ALTER TABLE `tblCons` ADD COLUMN `InheritEnableRdsAadAuth` tinyint NOT NULL DEFAULT 0",
        ];

        SqlMigrationHelper.ExecuteMigrationIdempotent(_databaseConnector, _version, msSqlAlter, mySqlAlters);
        return _version;
    }
}