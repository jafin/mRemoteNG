using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class SqlConnectionsSaver : ISaver<ConnectionTreeModel>
{
    private readonly SaveFilter _saveFilter;
    private readonly ISerializer<IEnumerable<LocalConnectionPropertiesModel>, string> _localPropertiesSerializer;
    private readonly IDataProvider<string> _dataProvider;

    public SqlConnectionsSaver(SaveFilter saveFilter, ISerializer<IEnumerable<LocalConnectionPropertiesModel>, string> localPropertieSerializer, IDataProvider<string> localPropertiesDataProvider)
    {
        ArgumentNullException.ThrowIfNull(saveFilter);
        ArgumentNullException.ThrowIfNull(localPropertieSerializer);
        ArgumentNullException.ThrowIfNull(localPropertiesDataProvider);
        _saveFilter = saveFilter;
        _localPropertiesSerializer = localPropertieSerializer;
        _dataProvider = localPropertiesDataProvider;
    }

    public void Save(ConnectionTreeModel connectionTreeModel, string propertyNameTrigger = "")
    {
        RootNodeInfo? rootTreeNode = connectionTreeModel.RootNodes.OfType<RootNodeInfo>().FirstOrDefault();
        if (rootTreeNode == null)
        {
            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, "SQL save aborted: connection tree has no root node.");
            return;
        }

        UpdateLocalConnectionProperties(rootTreeNode);

        if (PropertyIsLocalOnly(propertyNameTrigger))
        {
            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, $"Property {propertyNameTrigger} is local only. Not saving to database.");
            return;
        }

        if (SqlUserIsReadOnly())
        {
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, "Trying to save connection tree but the SQL read only checkbox is checked, aborting!");
            return;
        }

        using (IDatabaseConnector dbConnector = DatabaseConnectorFactory.DatabaseConnectorFromSettings())
        {
            dbConnector.Connect();
            SqlDatabaseVersionVerifier databaseVersionVerifier = new(dbConnector);
            SqlDatabaseMetaDataRetriever metaDataRetriever = new();
            SqlConnectionListMetaData? metaData = metaDataRetriever.GetDatabaseMetaData(dbConnector);

            // metaData == null means a brand-new database whose schema was just
            // initialized (tblRoot exists but has no rows yet). In that case skip
            // the version check — WriteDatabaseMetaData will insert the tblRoot row
            // with the current version during this save. (#1883)
            if (metaData != null && !databaseVersionVerifier.VerifyDatabaseVersion(metaData.ConfVersion))
            {
                Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, Language.ErrorConnectionListSaveFailed);
                throw new InvalidOperationException(Language.ErrorConnectionListSaveFailed);
            }

            ThrowIfTheDatabaseStillStoresSecretsWeakly(metaData);

            // Safety check: prevent truncating a non-empty database when the in-memory
            // tree is empty — this indicates a failed or incomplete load (#1351)
            int connectionCount = rootTreeNode.GetRecursiveChildList().Count();
            if (connectionCount == 0)
            {
                SqlDataProvider checkProvider = new(dbConnector);
                DataTable existingData = checkProvider.Load();
                if (existingData.Rows.Count > 0)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                        $"SQL save aborted: in-memory connection tree is empty but database contains " +
                        $"{existingData.Rows.Count} connection(s). This may indicate the connection tree " +
                        "was not loaded properly. Database data has been preserved. (See issue #1351)");
                    return;
                }
            }

            using DbTransaction transaction = dbConnector.DbConnection().BeginTransaction();
            try
            {
                // Null for a brand-new database, whose metadata row this save is about to insert
                // at the authenticated version. The rows below are written with whatever provider
                // that version selects, so the two always agree — a database created legacy and
                // written AEAD, or the reverse, is the state that cannot be recovered from.
                Version databaseVersion = metaData?.ConfVersion
                    ?? CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion;

                metaDataRetriever.WriteDatabaseMetaData(rootTreeNode, dbConnector, transaction, databaseVersion);
                UpdateConnectionsTable(rootTreeNode, dbConnector, databaseVersion, transaction);
                UpdateUpdatesTable(dbConnector, transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Runtime.MessageCollector.AddExceptionStackTrace(Language.ErrorConnectionListSaveFailed, ex);
                throw;
            }
        }

        Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, "Saved connections to database");
    }

    /// <summary>
    /// Refuses to write connections into a database that still stores its secrets with the legacy
    /// provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every save into a legacy database writes passwords under an unsalted MD5 key with no
    /// authentication tag</b> — recoverable at GPU speed, and modifiable by anyone with write access
    /// to the table. Continuing to write it is not a neutral act: it is the defect this change
    /// exists to remove, applied to whatever the user just typed.
    /// </para>
    /// <para>
    /// <b>Why refusing, when a classic connection file is still writable.</b> The connection file
    /// can offer its owner a choice, because the person prompted is the person affected. A database
    /// is shared: upgrading it decides for a whole team, cannot be undone without restoring a
    /// backup, and locks out every client not yet upgraded — so it is deliberately never prompted
    /// (see design.md). With no prompt available, refusing the write is the only way the decision
    /// gets made at all, rather than the weak format persisting indefinitely because nobody looked
    /// in the options page.
    /// </para>
    /// <para>
    /// <b>It is loud on purpose.</b> This throws rather than returning, because a save that quietly
    /// does nothing is worse than the encryption it declined to use: the user believes their change
    /// is stored. The message names the remedy and says the existing connections are unharmed,
    /// which is the first thing anyone will want to know.
    /// </para>
    /// <para>
    /// A null <paramref name="metaData"/> is a database being created by this very save, which
    /// <see cref="SqlDatabaseMetaDataRetriever"/> creates at the authenticated version. There is
    /// nothing to refuse and nothing to upgrade.
    /// </para>
    /// </remarks>
    private static void ThrowIfTheDatabaseStillStoresSecretsWeakly(SqlConnectionListMetaData? metaData)
    {
        if (metaData is null ||
            CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(metaData.ConfVersion))
            return;

        string message = string.Format(CultureInfo.InvariantCulture,
            Language.ErrorDatabaseNotUpgradedForEncryption, GeneralAppInfo.ProductName);

        Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, message);
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Determines if a given property name should be only saved
    /// locally.
    /// </summary>
    /// <param name="property">
    /// The name of the property that triggered the save event
    /// </param>
    /// <returns></returns>
    private static bool PropertyIsLocalOnly(string property)
    {
        return property == nameof(ConnectionInfo.OpenConnections) ||
               property == nameof(ContainerInfo.IsExpanded) ||
               property == nameof(ContainerInfo.Favorite);
    }

    private void UpdateLocalConnectionProperties(ContainerInfo rootNode)
    {
        IEnumerable<LocalConnectionPropertiesModel> a = rootNode.GetRecursiveChildList().Select(info => new LocalConnectionPropertiesModel
        {
            ConnectionId = info.ConstantID,
            Connected = info.OpenConnections.Count > 0,
            Expanded = info is ContainerInfo c && c.IsExpanded,
            Favorite = info.Favorite,
        });

        string serializedProperties = _localPropertiesSerializer.Serialize(a);
        _dataProvider.Save(serializedProperties);
        Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, "Saved local connection properties");
    }

    private void UpdateConnectionsTable(RootNodeInfo rootTreeNode, IDatabaseConnector databaseConnector,
                                        Version? databaseVersion, DbTransaction? transaction = null)
    {
        SqlDataProvider dataProvider = new(databaseConnector);
        DataTable currentDataTable = dataProvider.Load(transaction);

        // The database decides, exactly as it does on the read side. Writing with whatever this
        // build prefers is how the two ends came to disagree in the first place.
        ICryptographyProvider cryptoProvider = CryptoProviderFactoryFromSqlVersion.ProviderFor(databaseVersion);
        DataTableSerializer serializer = new(_saveFilter, cryptoProvider, rootTreeNode.PasswordString.ConvertToSecureString());
        serializer.SetSourceDataTable(currentDataTable);

        DataTable dataTable = serializer.Serialize(rootTreeNode);

        dataProvider.Save(dataTable, transaction);
    }

    private static void UpdateUpdatesTable(IDatabaseConnector databaseConnector, DbTransaction? transaction = null)
    {
        bool mustDisposeTransaction = false;
        if (transaction == null)
        {
            transaction = databaseConnector.DbConnection().BeginTransaction();
            mustDisposeTransaction = true;
        }

        try
        {
            SqlSafeUpdateHelper.DeleteAllRows(
                databaseConnector,
                transaction,
                "DELETE FROM tblUpdate",
                "DELETE FROM tblUpdate LIMIT 1");

            DbCommand dbQuery = databaseConnector.DbCommand("INSERT INTO tblUpdate (LastUpdate) VALUES(@LastUpdate)");
            dbQuery.Transaction = transaction;

            DbParameter lastUpdateParam = dbQuery.CreateParameter();
            lastUpdateParam.ParameterName = "@LastUpdate";
            lastUpdateParam.Value = MiscTools.DbTimeStampNow();
            dbQuery.Parameters.Add(lastUpdateParam);

            dbQuery.ExecuteNonQuery();

            if (mustDisposeTransaction)
            {
                transaction.Commit();
            }
        }
        catch
        {
            if (mustDisposeTransaction)
            {
                transaction.Rollback();
            }
            throw;
        }
        finally
        {
            if (mustDisposeTransaction)
            {
                transaction.Dispose();
            }
        }
    }

    private static bool SqlUserIsReadOnly()
    {
        return Properties.OptionsDBsPage.Default.SQLReadOnly;
    }
}