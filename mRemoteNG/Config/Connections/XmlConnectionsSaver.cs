using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Xml;
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
            ThrowIfExistingFileLevelIsUnrecognised();

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

    /// <summary>
    /// Refuses to write over a store whose declared format level this build cannot resolve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deserializer refuses to read such a file, and every save path is gated behind
    /// <c>IsConnectionsFileLoaded</c>, which a failed load clears — so this is the second lock on a
    /// door already shut. It is here because of what a save costs if the first one is ever bypassed:
    /// <see cref="FileDataProviderWithRollingBackup"/> copies the file before writing, so a save
    /// that should not have happened destroys the original <i>and</i> spends a backup slot on the
    /// result. The check is cheap and the failure is not recoverable.
    /// </para>
    /// <para>
    /// Reads only as far as the root element, so the cost does not scale with the file. A file that
    /// is absent, unreadable or not XML is not this method's business: only a positively-read,
    /// positively-unrecognised level refuses the write.
    /// </para>
    /// </remarks>
    private void ThrowIfExistingFileLevelIsUnrecognised()
    {
        string? recordedLevel;

        try
        {
            if (!File.Exists(_connectionFileName))
                return;

            using XmlReader reader = XmlReader.Create(_connectionFileName,
                new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });

            // The root element, whatever it is called: this fork writes it namespaced as
            // mrng:Connections while upstream writes a bare Connections, so matching by name would
            // silently skip one of the two shapes and check nothing.
            if (!reader.Read() || !reader.MoveToContent().Equals(XmlNodeType.Element))
                return;

            recordedLevel = reader.GetAttribute(StorageFormat.AttributeName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Not readable, so not judgeable. The save proceeds and fails on its own terms if the
            // path is genuinely broken; refusing here would block saves on any unrelated read fault.
            return;
        }

        if (StorageFormat.IsRecognised(recordedLevel))
            return;

        throw new NotSupportedException(
            $"Refusing to save over '{_connectionFileName}': it declares storage format " +
            $"'{recordedLevel}', which this build does not recognise. The file was written by a " +
            "newer version and overwriting it would discard that.");
    }
}