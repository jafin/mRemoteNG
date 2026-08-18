using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.Authentication;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class SqlConnectionsLoader : IConnectionsLoader
{
    private readonly IDeserializer<string, IEnumerable<LocalConnectionPropertiesModel>> _localConnectionPropertiesDeserializer;
    private readonly IDataProvider<string> _localPropertiesDataProvider;
    private readonly IDatabaseConnector _databaseConnector;
    private readonly IDataProvider<DataTable> _sqlDataProvider;
    private readonly ISqlDatabaseMetaDataRetriever _sqlMetaDataRetriever;
    private readonly ISqlDatabaseVersionVerifier _sqlDatabaseVersionVerifier;
    private readonly Func<Version?, ICryptographyProvider> _cryptographyProviderForVersion;

    private Func<string, Optional<SecureString>> AuthenticationRequestor { get; }

    public SqlConnectionsLoader(
        IDeserializer<string, IEnumerable<LocalConnectionPropertiesModel>> localConnectionPropertiesDeserializer,
        IDataProvider<string> localPropertiesDataProvider,
        IDatabaseConnector databaseConnector,
        IDataProvider<DataTable> sqlDataProvider,
        ISqlDatabaseMetaDataRetriever sqlMetaDataRetriever,
        ISqlDatabaseVersionVerifier sqlDatabaseVersionVerifier,
        Func<Version?, ICryptographyProvider> cryptographyProviderForVersion,
        Func<string, Optional<SecureString>>? authenticationRequestor = null)
    {
        ArgumentNullException.ThrowIfNull(localConnectionPropertiesDeserializer);
        ArgumentNullException.ThrowIfNull(localPropertiesDataProvider);
        ArgumentNullException.ThrowIfNull(databaseConnector);
        ArgumentNullException.ThrowIfNull(sqlDataProvider);
        ArgumentNullException.ThrowIfNull(sqlMetaDataRetriever);
        ArgumentNullException.ThrowIfNull(sqlDatabaseVersionVerifier);
        ArgumentNullException.ThrowIfNull(cryptographyProviderForVersion);
        _localConnectionPropertiesDeserializer = localConnectionPropertiesDeserializer;
        _localPropertiesDataProvider = localPropertiesDataProvider;
        _databaseConnector = databaseConnector;
        _sqlDataProvider = sqlDataProvider;
        _sqlMetaDataRetriever = sqlMetaDataRetriever;
        _sqlDatabaseVersionVerifier = sqlDatabaseVersionVerifier;
        _cryptographyProviderForVersion = cryptographyProviderForVersion;
        AuthenticationRequestor = authenticationRequestor ?? ((filename) => MiscTools.PasswordDialog(filename, false));
    }

    public ConnectionTreeModel Load()
    {
        SqlConnectionListMetaData metaData = _sqlMetaDataRetriever.GetDatabaseMetaData(_databaseConnector)
                                            ?? EmptyDatabase();

        bool versionSupported = _sqlDatabaseVersionVerifier.VerifyDatabaseVersion(metaData.ConfVersion);

        // A database newer than this build is refused rather than read. Its rows may be encrypted
        // or shaped in ways this client has no code for, and reading them anyway yields plausible
        // nonsense — connections with blank passwords — which a user reads as data loss rather than
        // as a version mismatch. The verifier has already said so on the message channel.
        //
        // Checked before the key, so a database this build cannot read does not first ask for a
        // master password. Its sentinel may not even be in a shape this build recognises, in which
        // case authentication fails first and a version mismatch reaches the user as a rejected
        // password on a database they have the password to.
        //
        // A database that is merely too old to upgrade is left as it was: still attempted, because
        // refusing it would lock out installations that work today.
        if (!versionSupported && _sqlDatabaseVersionVerifier.IsNewerThanSupported(metaData.ConfVersion))
            throw new InvalidOperationException("Could not load SQL connections");

        // Chosen from what the database records, not from what this build prefers, and chosen once
        // for both the sentinel and the rows — they are encrypted together and a mismatch between
        // them would authenticate a password that then decrypts nothing.
        ICryptographyProvider cryptographyProvider = _cryptographyProviderForVersion(metaData.ConfVersion);

        Optional<SecureString> decryptionKey = GetDecryptionKey(metaData, cryptographyProvider);

        if (!decryptionKey.Any())
            throw new InvalidOperationException("Could not load SQL connections");

        System.Data.DataTable dataTable = _sqlDataProvider.Load();
        DataTableDeserializer deserializer = new(cryptographyProvider, decryptionKey.First());
        ConnectionTreeModel connectionTree = deserializer.Deserialize(dataTable);
        ContainerInfo? rootNode = connectionTree.RootNodes.FirstOrDefault(i => i is RootNodeInfo);
        if (rootNode != null)
            ApplyLocalConnectionProperties(rootNode);
        return connectionTree;
    }

    /// <summary>
    /// The key this database is read with, prompting for a master password when it needs one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The built-in default key is a candidate only below the authenticated-encryption version.</b>
    /// It is four characters published in mRemoteNG's own source, so a database keyed with it is
    /// readable by anyone holding SELECT on the connections table — which on a team database is
    /// routinely more people than are trusted with the credentials it stores. Databases already in
    /// that state keep opening, because refusing them would destroy a team's access to their
    /// connections in order to change how those connections are stored; the pressure to upgrade
    /// belongs on the write path, which already refuses.
    /// </para>
    /// <para>
    /// At the new version the password is asked for before anything is tried, rather than by letting
    /// the default key fail first. Attempting it would spend one of the three attempts on a key this
    /// database is guaranteed not to use, and would leave the code able to succeed with it if the
    /// version gate above were ever weakened.
    /// </para>
    /// </remarks>
    private Optional<SecureString> GetDecryptionKey(SqlConnectionListMetaData metaData,
                                                    ICryptographyProvider cryptographyProvider)
    {
        string cipherText = metaData.Protected;
        bool requiresMasterPassword =
            CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(metaData.ConfVersion);
        SecureString defaultKey = new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();

        if (string.IsNullOrEmpty(cipherText))
        {
            // An empty sentinel means two different things, and the version is what separates them.
            //
            // Below the new version it means this database has no master password: an unprotected
            // legacy store is keyed on the default, and that is how it has always been read.
            //
            // At the new version it cannot mean that, because there is no unprotected state to
            // record — every database at this version was written with a master password. It means
            // the metadata row was lost or replaced, and there is nothing left to check a password
            // against. Returning the default key here would open a database that is meant to require
            // one, which is the whole defect this exists to close.
            if (!requiresMasterPassword)
                return defaultKey;

            Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, Language.ErrorSqlDatabaseNotInitialized);
            return Optional<SecureString>.Empty;
        }

        // The sentinel is checked by its contents, not merely by decrypting without error. The
        // legacy provider is AES-CBC with no authentication tag, so a wrong password yields valid
        // padding often enough to matter and would otherwise be accepted — granting access to the
        // hostnames, usernames and ports, which are not encrypted at all.
        PasswordAuthenticator authenticator = new(cryptographyProvider, cipherText, () => AuthenticationRequestor(""))
        {
            PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel
        };

        SecureString firstCandidate;

        if (requiresMasterPassword)
        {
            Optional<SecureString> supplied = AuthenticationRequestor("");

            // Declining the prompt is declining to open the database. Falling through to the default
            // key would be the fallback this change removes, arrived at by a different route.
            if (!supplied.Any() || supplied.First() is not { Length: > 0 } typed)
                return Optional<SecureString>.Empty;

            firstCandidate = typed;
        }
        else
        {
            firstCandidate = defaultKey;
        }

        bool authenticated = authenticator.Authenticate(firstCandidate);

        return authenticated && authenticator.LastAuthenticatedPassword is { } password
            ? password
            : Optional<SecureString>.Empty;
    }

    private void ApplyLocalConnectionProperties(ContainerInfo rootNode)
    {
        string localPropertiesXml = _localPropertiesDataProvider.Load();
        IEnumerable<LocalConnectionPropertiesModel> localConnectionProperties = _localConnectionPropertiesDeserializer.Deserialize(localPropertiesXml);

        rootNode
            .GetRecursiveChildList()
            .Join(localConnectionProperties,
                con => con.ConstantID,
                locals => locals.ConnectionId,
                (con, locals) => new {Connection = con, LocalProperties = locals},
                StringComparer.Ordinal)
            .ForEach(x =>
            {
                x.Connection.PleaseConnect = x.LocalProperties.Connected;
                x.Connection.Favorite = x.LocalProperties.Favorite;
                if (x.Connection is ContainerInfo container)
                    container.IsExpanded = x.LocalProperties.Expanded;
            });
    }

    /// <summary>
    /// What to read when the database holds no metadata row: nothing, from an empty table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to write the row, and could not write a usable one.</b> It created a metadata
    /// row from a root node with no master password — which at the authenticated version is the one
    /// state that cannot be recorded, because the only key available is the constant published in
    /// this application's source. The database was left claiming a format nothing held the key to.
    /// </para>
    /// <para>
    /// Nothing is lost by not writing it. The schema is created by the metadata read itself; the row
    /// is written by the first save, which has the user's own tree and therefore its master
    /// password. Until then there is nothing in the database to load, and this describes exactly
    /// that: the schema version this build writes, so the legacy provider, and no sentinel, so no
    /// password is asked for on a database nothing has ever written to.
    /// </para>
    /// </remarks>
    private static SqlConnectionListMetaData EmptyDatabase() =>
        new()
        {
            Name = "Connections",
            Protected = "",
            Export = false,
            ConfVersion = SqlDatabaseVersionVerifier.SchemaVersion
        };
}