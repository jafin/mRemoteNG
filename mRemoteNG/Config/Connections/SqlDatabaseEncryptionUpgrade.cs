using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Security;
using mRemoteNG.Security.Authentication;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Connections;

/// <summary>
/// Re-encrypts every secret in a SQL database and raises its version, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only thing that moves a database between the two formats, and it is never
/// automatic.</b> Not the first save, not a prompt when the database is opened — a deliberate
/// command, because upgrading changes the format for a whole team, cannot be undone without
/// restoring a backup, and locks out every client still on an older build, upstream mRemoteNG
/// included. Whoever opens the application first is not necessarily entitled to decide that.
/// </para>
/// <para>
/// <b>One transaction, and the reason is the state it prevents.</b> A half-migrated table would
/// leave some rows recoverable at GPU speed and nothing to say which — the version marker would
/// claim one thing and the contents be a mixture. A rollback leaves a database that is entirely
/// legacy and entirely readable, which is exactly where it started.
/// </para>
/// <para>
/// The master password is authenticated against the <c>Protected</c> sentinel before a single row is
/// touched. Without that check a wrong password would decrypt every secret to rubbish and then write
/// that rubbish back under the new format, destroying the database with no error at any point —
/// AES-CBC has no authentication tag, so it does not refuse a wrong key.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class SqlDatabaseEncryptionUpgrade
{
    /// <summary>The columns holding secrets. Everything else in the row is stored in the clear.</summary>
    private static readonly string[] SecretColumns = ["Password", "RDGatewayPassword", "VNCProxyPassword"];

    /// <summary>
    /// Whether this database has anything to upgrade.
    /// </summary>
    public static bool IsAvailableFor(SqlConnectionListMetaData? metaData) =>
        metaData is not null &&
        !CryptoProviderFactoryFromSqlVersion.UsesAuthenticatedEncryption(metaData.ConfVersion);

    /// <summary>
    /// Whether the caller must collect a master password before calling <see cref="Apply"/>.
    /// </summary>
    /// <remarks>
    /// Asked so that a database with no master password does not put a password box in front of
    /// someone who has never had one to type. It is answered by trying the built-in default key
    /// against the sentinel rather than by guessing from the presence of the column: <c>Protected</c>
    /// is never empty in a real database — an unprotected store holds "ThisIsNotProtected" encrypted
    /// under the default key, not nothing at all.
    /// <para>
    /// A wrong answer here is harmless, which is why this can be a convenience rather than a
    /// safeguard: <see cref="Apply"/> authenticates whatever it is handed and refuses on its own.
    /// </para>
    /// </remarks>
    public static bool RequiresMasterPassword(SqlConnectionListMetaData metaData)
    {
        ArgumentNullException.ThrowIfNull(metaData);

        if (string.IsNullOrEmpty(metaData.Protected))
            return false;

        ICryptographyProvider legacy = CryptoProviderFactoryFromSqlVersion.ProviderFor(metaData.ConfVersion);
        SecureString defaultKey = new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();

        try
        {
            return !ConnectionFileDefaults.IsKnownSentinel(legacy.Decrypt(metaData.Protected, defaultKey));
        }
        catch (EncryptionException)
        {
            return true;
        }
    }

    /// <summary>
    /// Re-encrypts every secret column with authenticated encryption and raises the recorded version.
    /// </summary>
    /// <param name="existingPassword">
    /// Opens the database as it stands. The built-in default key for a database that has no master
    /// password. Authenticated against the stored sentinel before a row is touched. Not disposed
    /// here — the caller supplied it and is the only thing that knows whether it is still needed.
    /// </param>
    /// <param name="newMasterPassword">
    /// What the upgraded database is keyed on. The same password as <paramref name="existingPassword"/>
    /// for a database that already has one; a newly chosen password for a database that does not.
    /// </param>
    /// <returns>
    /// The number of rows rewritten. Zero is a legitimate answer for an empty database, which is
    /// still upgraded: the version marker moves, so the next save writes the new format.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The database is already upgraded, has no metadata row, or no master password was supplied.
    /// </exception>
    /// <exception cref="EncryptionException">The existing password does not open this database.</exception>
    /// <remarks>
    /// <b>The upgrade sets a master password, it does not merely preserve one.</b> A database with
    /// none is keyed on a constant published in this application's source, and re-encrypting every
    /// secret under that same constant would satisfy the letter of "upgraded to AES-256-GCM" while
    /// leaving every password in the database readable by anyone who can read the table. The cipher
    /// was never the only weakness; the key was the larger one.
    /// </remarks>
    public static int Apply(IDatabaseConnector databaseConnector,
                            SecureString existingPassword,
                            SecureString newMasterPassword,
                            ISqlDatabaseMetaDataRetriever metaDataRetriever)
    {
        ArgumentNullException.ThrowIfNull(databaseConnector);
        ArgumentNullException.ThrowIfNull(existingPassword);
        ArgumentNullException.ThrowIfNull(newMasterPassword);
        ArgumentNullException.ThrowIfNull(metaDataRetriever);

        if (newMasterPassword.Length == 0)
            throw new ArgumentException(
                "An upgraded database is keyed on a master password. Without one this would " +
                "re-encrypt every secret under the built-in default key, which is published and " +
                "therefore no protection at all.", nameof(newMasterPassword));

        // Typing the default key as the master password reaches the same place by hand. Caught
        // here, because further down it stops looking like a password decision: the root node's
        // own setter reads that value as "no password set", and the refusal would arrive as a
        // metadata writer complaining about a tree the user never touched.
        if (string.Equals(newMasterPassword.ConvertToUnsecureString(),
                          ConnectionFileDefaults.LegacyEncryptionKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "That is the built-in default key, published in this application's source. A " +
                "database keyed on it is readable by anyone who can read the table.",
                nameof(newMasterPassword));
        }

        SqlConnectionListMetaData? metaData = metaDataRetriever.GetDatabaseMetaData(databaseConnector);

        if (!IsAvailableFor(metaData))
            throw new ArgumentException(
                "This database is already storing its secrets with authenticated encryption, or has " +
                "no metadata row to read. There is nothing to upgrade.", nameof(databaseConnector));

        ICryptographyProvider legacy = CryptoProviderFactoryFromSqlVersion.ProviderFor(metaData!.ConfVersion);
        ICryptographyProvider authenticated = CryptoProviderFactoryFromSqlVersion.ProviderFor(
            CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion);

        SecureString readKey = Authenticate(metaData, existingPassword, legacy);

        using DbTransaction transaction = databaseConnector.DbConnection().BeginTransaction();
        try
        {
            int rewritten = RewriteSecrets(databaseConnector, transaction, legacy, authenticated,
                                           readKey, newMasterPassword);

            // Last, and inside the same transaction. The version marker is what tells every future
            // reader which provider to use, so it must not be raised over contents that have not
            // been rewritten — nor left behind after contents that have.
            metaDataRetriever.WriteDatabaseMetaData(RootFor(metaData, newMasterPassword), databaseConnector, transaction,
                CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion);

            transaction.Commit();
            return rewritten;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Checks the master password against the stored sentinel, and hands back the key to use.
    /// </summary>
    /// <remarks>
    /// By contents, not merely by decrypting without error: the legacy provider is AES-CBC with no
    /// authentication tag, so a wrong password yields valid padding often enough to matter and would
    /// otherwise be accepted. An empty sentinel means the database has no master password and is on
    /// the built-in default key — the subject of `require-sql-master-password`, not this.
    /// </remarks>
    private static SecureString Authenticate(SqlConnectionListMetaData metaData,
                                             SecureString masterPassword,
                                             ICryptographyProvider legacy)
    {
        SecureString defaultKey = new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();

        if (string.IsNullOrEmpty(metaData.Protected))
            return defaultKey;

        PasswordAuthenticator authenticator = new(legacy, metaData.Protected, () => new Optional<SecureString>(masterPassword))
        {
            PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel
        };

        if (authenticator.Authenticate(defaultKey) && authenticator.LastAuthenticatedPassword is { } authenticated)
            return authenticated;

        // Before a single row is touched. A wrong password here would decrypt every secret to
        // rubbish and write that rubbish back under the new format — the database destroyed, with
        // no error raised at any point along the way.
        throw new EncryptionException(Resources.Language.Language.ErrorDecryptionFailed);
    }

    private static int RewriteSecrets(IDatabaseConnector databaseConnector,
                                      DbTransaction transaction,
                                      ICryptographyProvider legacy,
                                      ICryptographyProvider authenticated,
                                      SecureString readKey,
                                      SecureString writeKey)
    {
        List<(string Id, Dictionary<string, string> Secrets)> rows = ReadSecrets(databaseConnector, transaction);
        int rewritten = 0;

        foreach ((string id, Dictionary<string, string> secrets) in rows)
        {
            Dictionary<string, string> reencrypted = [];

            foreach ((string column, string cipherText) in secrets)
            {
                // An empty column stays empty. Encrypting nothing would replace "this connection has
                // no gateway password" with a ciphertext that decrypts to an empty string — the same
                // meaning to this application, and a difference to anything comparing the columns.
                if (string.IsNullOrEmpty(cipherText))
                    continue;

                reencrypted[column] = authenticated.Encrypt(legacy.Decrypt(cipherText, readKey), writeKey);
            }

            if (reencrypted.Count == 0)
                continue;

            Update(databaseConnector, transaction, id, reencrypted);
            rewritten++;
        }

        return rewritten;
    }

    private static List<(string Id, Dictionary<string, string> Secrets)> ReadSecrets(
        IDatabaseConnector databaseConnector, DbTransaction transaction)
    {
        List<(string, Dictionary<string, string>)> rows = [];

        using DbCommand command = databaseConnector.DbCommand(
            $"SELECT ConstantID, {string.Join(", ", SecretColumns)} FROM tblCons");
        command.Transaction = transaction;

        // Read to completion before updating. Holding a reader open on the same connection while
        // issuing updates through it is what MultipleActiveResultSets exists to permit, and relying
        // on a connection-string option for correctness is how this would break against a backend
        // that does not offer one.
        using (DbDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                Dictionary<string, string> secrets = [];
                foreach (string column in SecretColumns)
                    secrets[column] = reader[column] as string ?? string.Empty;

                rows.Add(((string)reader["ConstantID"], secrets));
            }
        }

        return rows;
    }

    private static void Update(IDatabaseConnector databaseConnector, DbTransaction transaction,
                               string id, Dictionary<string, string> secrets)
    {
        // Column names come from this file's own constant list and never from data; every value is
        // parameterised.
        string assignments = string.Join(", ", secrets.Keys.Select(c => $"{c} = @{c}"));

        using DbCommand command = databaseConnector.DbCommand(
            $"UPDATE tblCons SET {assignments} WHERE ConstantID = @ConstantID");
        command.Transaction = transaction;

        foreach ((string column, string value) in secrets)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@" + column;
            parameter.DbType = System.Data.DbType.String;
            parameter.Size = -1;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        DbParameter idParameter = command.CreateParameter();
        idParameter.ParameterName = "@ConstantID";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A root node carrying the name the database already records and the master password it is
    /// being keyed on, so rewriting the metadata row preserves the one and establishes the other.
    /// </summary>
    /// <remarks>
    /// <b>The key has to come with it.</b> <c>WriteDatabaseMetaData</c> encrypts the sentinel with
    /// the root node's own <c>PasswordString</c>, so a root carrying "this store is protected" and
    /// nothing else writes a sentinel under the *default* password — and the upgraded database then
    /// refuses the master password it was upgraded with. The test that caught this decrypts the
    /// rewritten sentinel; without it the failure would have arrived as a database nobody could open.
    /// <para>
    /// The database's previous state is not consulted. Whether it had a master password decides what
    /// the caller must collect, not what is written: at this version there is only one answer, and a
    /// root that said otherwise would write the unprotected sentinel — which the metadata writer now
    /// refuses outright.
    /// </para>
    /// <para>
    /// Materialising the key as a string is forced by that API taking a root node. It is the same
    /// key already held in memory for the whole re-encryption, so this widens nothing that was not
    /// already open, and narrowing it means changing what the metadata writer accepts.
    /// </para>
    /// </remarks>
    private static RootNodeInfo RootFor(SqlConnectionListMetaData metaData, SecureString masterPassword) =>
        new(RootNodeType.Connection)
        {
            Name = metaData.Name,
            Password = true,
            PasswordString = masterPassword.ConvertToUnsecureString()
        };
}
