using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Credential;
using mRemoteNG.Security;

namespace mRemoteNG.Config.Import;

public static class CredentialImportHelper
{
    public static void ExtractCredentials(ConnectionInfo connection, ICredentialRepository repository)
    {
        // Check if this specific node has credentials to extract
        if (!string.IsNullOrEmpty(connection.Username) ||
            !string.IsNullOrEmpty(connection.Domain) ||
            connection.HasPassword)
        {
            CredentialRecord record = new()
            {
                Title = string.IsNullOrWhiteSpace(connection.Name) ? "Imported Credential" : connection.Name,
                Username = connection.Username,
                // The connection's secret handed over without a plain-text copy in between. It was
                // read as a string and converted straight back, which produced an unzeroable copy of
                // every password in the file being imported.
                Password = connection.SecurePassword,
                Domain = connection.Domain
            };

            repository.CredentialRecords.Add(record);
            connection.CredentialId = record.Id.ToString();

            // Clear local credentials after extraction
            connection.Username = string.Empty;
            connection.Password = string.Empty;
            connection.Domain = string.Empty;
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
            connection.HasPassword)
        {
            return true;
        }

        if (connection is ContainerInfo container)
        {
            return container.Children.Any(HasCredentials);
        }

        return false;
    }
}