using System;
using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.App;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.Security.KeyDerivation;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class XmlConnectionsSaver : ISaver<ConnectionTreeModel>
{
    private readonly string _connectionFileName;
    private readonly SaveFilter _saveFilter;

    public XmlConnectionsSaver(string connectionFileName, SaveFilter saveFilter)
    {
        if (string.IsNullOrEmpty(connectionFileName))
            throw new ArgumentException($"Argument '{nameof(connectionFileName)}' cannot be null or empty", nameof(connectionFileName));
        _connectionFileName = connectionFileName;
        _saveFilter = saveFilter ?? throw new ArgumentNullException(nameof(saveFilter));
    }

    public void Save(ConnectionTreeModel connectionTreeModel, string propertyNameTrigger = "")
    {
        try
        {
            ICryptographyProvider cryptographyProvider = new CryptoProviderFactoryFromSettings().Build();

            RootNodeInfo? rootNode = connectionTreeModel.RootNodes.OfType<RootNodeInfo>().FirstOrDefault();
            if (rootNode == null)
                throw new InvalidOperationException("Connection tree has no root node");

            // The only place that knows both the provider and the store's format level. A classic
            // store keeps deriving with SHA-1 so upstream mRemoteNG, which reads this same file from
            // this same path, can still open it; the stronger function is what being hardened buys.
            cryptographyProvider.KeyDerivationPrf = rootNode.StorageFormat == StorageFormatLevel.Hardened
                ? KeyDerivationPrf.Hardened
                : KeyDerivationPrf.Default;

            Serializers.ISerializer<Connection.ConnectionInfo, string> xmlConnectionsSerializer = XmlConnectionSerializerFactory.Build(cryptographyProvider, connectionTreeModel, _saveFilter, Properties.OptionsSecurityPage.Default.EncryptCompleteConnectionsFile);
            string xml = xmlConnectionsSerializer.Serialize(rootNode);

            if (string.IsNullOrEmpty(xml))
                throw new InvalidOperationException("Serialized XML is empty");

            FileDataProviderWithRollingBackup fileDataProvider = new(_connectionFileName);
            fileDataProvider.Save(xml);
        }
        catch (Exception ex)
        {
            // Logged here for the stack trace, then rethrown: only the caller knows whether
            // this write was the user's connection file — and so whether they need telling.
            Runtime.MessageCollector?.AddExceptionStackTrace("SaveToXml failed", ex);
            throw;
        }
    }
}