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
            Serializers.ISerializer<Connection.ConnectionInfo, string> xmlConnectionsSerializer = XmlConnectionSerializerFactory.Build(cryptographyProvider, connectionTreeModel, _saveFilter, Properties.OptionsSecurityPage.Default.EncryptCompleteConnectionsFile);

            RootNodeInfo? rootNode = connectionTreeModel.RootNodes.OfType<RootNodeInfo>().FirstOrDefault();
            if (rootNode == null)
                throw new InvalidOperationException("Connection tree has no root node");
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