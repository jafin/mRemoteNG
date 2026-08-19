using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;

[SupportedOSPlatform("windows")]
public class XmlConnectionsSerializer(ICryptographyProvider cryptographyProvider,
    ISerializer<ConnectionInfo, XElement> connectionNodeSerializer) : ISerializer<ConnectionTreeModel, string>,
    ISerializer<ConnectionInfo, string>
{
    private readonly ICryptographyProvider _cryptographyProvider = cryptographyProvider;
    private readonly ISerializer<ConnectionInfo, XElement> _connectionNodeSerializer = connectionNodeSerializer;

    public Version Version => _connectionNodeSerializer.Version;
    public bool UseFullEncryption { get; set; }

    /// <summary>
    /// The storage format level to write, instead of the one the root node carries.
    /// </summary>
    /// <remarks>
    /// Set by callers producing a copy rather than the store itself. An export must stay readable by
    /// upstream mRemoteNG whatever the store it came from is.
    /// </remarks>
    public StorageFormatLevel? StorageFormatOverride { get; set; }

    public string Serialize(ConnectionTreeModel connectionTreeModel)
    {
        RootNodeInfo rootNode = (RootNodeInfo)connectionTreeModel.RootNodes.First(node => node is RootNodeInfo);
        return SerializeConnectionsData(rootNode);
    }

    public string Serialize(ConnectionInfo serializationTarget)
    {
        return SerializeConnectionsData(serializationTarget);
    }

    private string SerializeConnectionsData(ConnectionInfo serializationTarget)
    {
        string xml = "";
        try
        {
            XmlConnectionsDocumentCompiler documentCompiler =
                new(_cryptographyProvider, _connectionNodeSerializer)
                {
                    StorageFormatOverride = StorageFormatOverride
                };
            XDocument xmlDocument = documentCompiler.CompileDocument(serializationTarget, UseFullEncryption);
            xml = WriteXmlToString(xmlDocument);
        }
        catch (ConnectionSecretDecryptionException)
        {
            // Let out rather than swallowed into an empty document. Everything else here is reported
            // and leaves the caller to refuse the write on an empty string, which says nothing about
            // why; this one is the store failing to re-encrypt a secret it could not read, and the
            // user needs to be told which connection it was so the write can be refused honestly.
            throw;
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace("SaveToXml failed", ex);
        }

        return xml;
    }

    private static string WriteXmlToString(XNode xmlDocument)
    {
        string xmlString;
        XmlWriterSettings xmlWriterSettings = new() { Indent = true, IndentChars = "    ", Encoding = Encoding.UTF8};
        MemoryStream memoryStream = new();
        using (XmlWriter xmlTextWriter = XmlWriter.Create(memoryStream, xmlWriterSettings))
        {
            xmlDocument.WriteTo(xmlTextWriter);
            xmlTextWriter.Flush();
            StreamReader streamReader = new(memoryStream, Encoding.UTF8, true);
            memoryStream.Seek(0, SeekOrigin.Begin);
            xmlString = streamReader.ReadToEnd();
        }

        return xmlString;
    }
}