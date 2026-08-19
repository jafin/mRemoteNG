using System.Linq;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Credential;
using mRemoteNG.Messages;
using mRemoteNG.Security;

namespace mRemoteNG.Config.Import;

public static class CredentialImportHelper
{
    public static void ExtractCredentials(ConnectionInfo connection, ICredentialRepository repository)
    {
        // Check if this specific node has credentials to extract
        if (!string.IsNullOrEmpty(connection.Username) ||
            !string.IsNullOrEmpty(connection.Domain) ||
            HasPassword(connection))
        {
            // Read before anything is written. This is the one read that can fail, the extraction
            // ends by clearing the connection's own password, and a failure after that clearing
            // would lose the secret outright — so the connection is left exactly as it is instead,
            // still holding a password nothing can currently read. The failure has already been
            // reported against it by name.
            SecureString? password = ReadPassword(connection);
            if (password is null)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.WarningMsg,
                    $"The password stored for '{connection.Name}' could not be read, so its " +
                    "credentials were left on the connection rather than extracted. Every other " +
                    "connection in this import was unaffected.", true);
            }
            else
            {
                CredentialRecord record = new()
                {
                    Title = string.IsNullOrWhiteSpace(connection.Name) ? "Imported Credential" : connection.Name,
                    Username = connection.Username,
                    // The connection's secret handed over without a plain-text copy in between. It was
                    // read as a string and converted straight back, which produced an unzeroable copy of
                    // every password in the file being imported.
                    Password = password,
                    Domain = connection.Domain
                };

                repository.CredentialRecords.Add(record);
                connection.CredentialId = record.Id.ToString();

                // Clear local credentials after extraction
                connection.Username = string.Empty;
                connection.Password = string.Empty;
                connection.Domain = string.Empty;
            }
        }

        // Recurse into children if it's a container
        if (connection is ContainerInfo container)
        {
            foreach (ConnectionInfo child in container.Children)
            {
                ExtractCredentials(child, repository);
            }
        }
    }

    public static bool HasCredentials(ConnectionInfo connection)
    {
        // The password is asked about last on purpose. The other two are string comparisons, and
        // this one may have to decrypt a stored secret to answer - which, over every node in a file
        // being imported, is the cost this whole change exists to avoid paying up front.
        if (!string.IsNullOrEmpty(connection.Username) ||
            !string.IsNullOrEmpty(connection.Domain) ||
            HasPassword(connection))
        {
            return true;
        }

        if (connection is ContainerInfo container)
        {
            return container.Children.Any(HasCredentials);
        }

        return false;
    }

    /// <summary>
    /// Whether the connection has a password, treating one that cannot be decrypted as one it has.
    /// </summary>
    /// <remarks>
    /// Both callers walk every node in the file being imported, so a secret that will not decrypt
    /// must not abort the walk: one damaged password would otherwise make the whole import report
    /// nothing at all. An unreadable secret is still a secret, and the failure has already been
    /// reported against the connection it belongs to by the read that met it.
    /// </remarks>
    private static bool HasPassword(ConnectionInfo connection)
    {
        try
        {
            return connection.HasPassword;
        }
        catch (ConnectionSecretDecryptionException)
        {
            return true;
        }
    }

    /// <summary>
    /// The connection's password, or <see langword="null"/> when it cannot be decrypted.
    /// </summary>
    private static SecureString? ReadPassword(ConnectionInfo connection)
    {
        try
        {
            return connection.SecurePassword;
        }
        catch (ConnectionSecretDecryptionException)
        {
            return null;
        }
    }
}