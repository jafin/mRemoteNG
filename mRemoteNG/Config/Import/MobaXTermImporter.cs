using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.Serializers.MiscSerializers;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Credential;

namespace mRemoteNG.Config.Import;

[SupportedOSPlatform("windows")]
public class MobaXTermImporter : IConnectionImporter<string>
{
    public void Import(string fileName, ContainerInfo destinationContainer)
    {
        string content = File.ReadAllText(fileName);

        MobaXTermSessionDeserializer deserializer = new();
        Tree.ConnectionTreeModel connectionTreeModel = deserializer.Deserialize(content);

        foreach (ConnectionInfo child in connectionTreeModel.RootNodes.First().Children.ToList())
        {
            if (Runtime.CredentialProviderCatalog.CredentialProviders.Any())
            {
                ICredentialRepository repository = Runtime.CredentialProviderCatalog.CredentialProviders.First();
                CredentialImportHelper.ExtractCredentials(child, repository);
            }

            destinationContainer.AddChild(child);
        }
    }
}