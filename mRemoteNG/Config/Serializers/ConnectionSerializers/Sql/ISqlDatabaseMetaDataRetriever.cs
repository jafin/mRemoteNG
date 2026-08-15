using System;
using System.Data.Common;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;

public interface ISqlDatabaseMetaDataRetriever
{
    SqlConnectionListMetaData? GetDatabaseMetaData(IDatabaseConnector databaseConnector);
    void WriteDatabaseMetaData(RootNodeInfo rootTreeNode, IDatabaseConnector databaseConnector);
    /// <param name="databaseVersion">
    /// The version the database records, which decides how the <c>Protected</c> sentinel is
    /// encrypted. Null means legacy — a brand-new database, or one whose metadata could not be read.
    /// </param>
    void WriteDatabaseMetaData(RootNodeInfo rootTreeNode, IDatabaseConnector databaseConnector,
                               DbTransaction? transaction, Version? databaseVersion = null);
}