using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
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

            WarnOnceIfTheDatabaseStillStoresSecretsWeakly(metaData, dbConnector);

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
                // at the authenticated version — nothing reads a database this build just created,
                // so there is nobody to stay compatible with and no reason to start it weak. An
                // existing database keeps whatever version it has. The rows below are written with
                // whatever provider that version selects, so the two always agree: a database
                // marked legacy and written AEAD, or the reverse, is the state nothing recovers
                // from.
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
    /// Databases already warned about in this session, so the warning is said once and not on every
    /// save.
    /// </summary>
    private static readonly HashSet<string> WarnedDatabases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock WarnedDatabasesLock = new();

    /// <summary>
    /// Says once, per database, that this database still stores its secrets with the legacy provider
    /// — and saves anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This deliberately does not refuse the write, and an earlier draft of this change did.</b>
    /// The proposal's "a database at the old version is read and not written" was a bigger hammer
    /// than its own reasoning called for. What design.md establishes is that the *upgrade* must not
    /// be prompted: it decides for a whole team, cannot be undone without restoring a backup, and
    /// locks out every client that has not been upgraded — so it must not be offered to whoever
    /// happens to open the application first. None of that establishes that a legacy database should
    /// stop accepting writes.
    /// </para>
    /// <para>
    /// Refusing would not make anyone safer today, either. The weak encryption is a state these
    /// databases are already in; declining to write does not improve it, it only stops people
    /// working until an administrator acts. Saves here are automatic and debounced, so it would
    /// surface as an error on every rename — experienced as a broken release rather than as a
    /// security improvement. It would also contradict this fork's own precedent: a classic
    /// connection file stays fully readable *and* writable, and hardening is offered rather than
    /// imposed.
    /// </para>
    /// <para>
    /// <b>Once per database, and never modal.</b> A save can run on the debounce timer rather than
    /// on the user's action, so a dialog here could appear over unrelated work or off the UI thread
    /// entirely. It goes to the message channel instead, once, naming the remedy and — the part a
    /// user needs before acting — that upgrading locks out older builds.
    /// </para>
    /// <para>
    /// The cost is real and worth stating: a team that never opens the SQL options page keeps the
    /// weak format indefinitely. That is what the options page's own status line is for, and it
    /// reaches the person who can actually decide.
    /// </para>
    /// </remarks>
    private static void WarnOnceIfTheDatabaseStillStoresSecretsWeakly(SqlConnectionListMetaData? metaData,
                                                                      IDatabaseConnector databaseConnector)
    {
        if (metaData is null ||
            CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(metaData.ConfVersion))
            return;

        // Identity rather than the connection string, which carries a password. Two databases on one
        // server are different stores and are warned about separately.
        string identity = $"{databaseConnector.DbConnection().DataSource}/{databaseConnector.DbConnection().Database}";

        lock (WarnedDatabasesLock)
        {
            if (!WarnedDatabases.Add(identity))
                return;
        }

        Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, string.Format(
            CultureInfo.InvariantCulture, Language.WarningDatabaseNotUpgradedForEncryption,
            GeneralAppInfo.ProductName));
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