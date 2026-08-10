using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Config.Connections;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Csv;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Json;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Rdp;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI.Forms;


namespace mRemoteNG.App;

[SupportedOSPlatform("windows")]
public static class Export
{
    /// <summary>
    /// Whether an export of this store, in this format, produces a copy protected more weakly than
    /// the store itself — and so has to say so before it is written.
    /// </summary>
    /// <remarks>
    /// Only a hardened store has anything to lose. Exporting a classic store produces a copy with
    /// exactly the store's own protection, and warning about that would be untrue as well as noise.
    /// <para>
    /// Limited to the connection-file format, which is the one an export is written in to be opened
    /// again as a store. The other formats are interchange formats that carry no protection at any
    /// level, so what they lose is not a property of the level and is not this message to make.
    /// </para>
    /// </remarks>
    public static bool ExportWeakensProtection(StorageFormatLevel storeLevel, SaveFormat saveFormat) =>
        storeLevel == StorageFormatLevel.Hardened && saveFormat == SaveFormat.mRXML;

    public static void ExportToFile(ConnectionInfo? selectedNode, ConnectionTreeModel connectionTreeModel)
    {
        try
        {
            SaveFilter saveFilter = new();

            using FrmExport exportForm = new();
            if (selectedNode?.GetTreeNodeType() == TreeNodeType.Container)
                exportForm.SelectedFolder = selectedNode as ContainerInfo;
            else if (selectedNode?.GetTreeNodeType() == TreeNodeType.Connection)
            {
                if (selectedNode.Parent?.GetTreeNodeType() == TreeNodeType.Container)
                    exportForm.SelectedFolder = selectedNode.Parent;
                exportForm.SelectedConnection = selectedNode;
            }

            if (exportForm.ShowDialog(FrmMain.Default) != DialogResult.OK)
                return;

            ConnectionInfo defaultTarget = connectionTreeModel.RootNodes.First(node => node is RootNodeInfo);

            // Said before the file is written, not after: the point of the export is that it is a
            // way back out of the hardened format, and a way back the user did not understand they
            // were weakening is not one they consented to.
            if (defaultTarget is RootNodeInfo storeRoot &&
                ExportWeakensProtection(storeRoot.StorageFormat, exportForm.SaveFormat) &&
                MessageBox.Show(FrmMain.Default, Language.ExportClassicFormatContent,
                    Language.ExportClassicFormatTitle, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var exportTarget = exportForm.Scope switch
            {
                FrmExport.ExportScope.SelectedFolder => exportForm.SelectedFolder ?? defaultTarget,
                FrmExport.ExportScope.SelectedConnection => exportForm.SelectedConnection ?? defaultTarget,
                _ => defaultTarget
            };

            saveFilter.SaveUsername = exportForm.IncludeUsername;
            saveFilter.SavePassword = exportForm.IncludePassword;
            saveFilter.SaveDomain = exportForm.IncludeDomain;
            saveFilter.SaveInheritance = exportForm.IncludeInheritance;
            saveFilter.SaveCredentialId = exportForm.IncludeAssignedCredential;

            SaveExportFile(exportForm.FileName, exportForm.SaveFormat, saveFilter, exportTarget, exportForm.IsEncrypted,
                exportForm.Password);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionMessage("App.Export.ExportToFile() failed.", ex);
        }
    }

    private static void SaveExportFile(string fileName,
        SaveFormat saveFormat,
        SaveFilter saveFilter,
        ConnectionInfo exportTarget,
        bool isEncrypted = false,
        string password = "")
    {
        try
        {
            ISerializer<ConnectionInfo, string> serializer;
            switch (saveFormat)
            {
                case SaveFormat.mRXML:
                    if (isEncrypted)
                    {
                        RootNodeInfo tempRoot = new(RootNodeType.Connection)
                        {
                            PasswordString = password
                        };

                        var clonedTarget = exportTarget.Clone();

                        if (exportTarget is RootNodeInfo)
                        {
                            if (clonedTarget is ContainerInfo container)
                            {
                                tempRoot.AddChildRange(container.Children.ToArray());
                            }

                            exportTarget = tempRoot;
                        }
                        else
                        {
                            tempRoot.AddChild(clonedTarget);
                            exportTarget = clonedTarget;
                        }
                    }

                    var cryptographyProvider = new CryptoProviderFactoryFromSettings().Build();
                    var rootNode = exportTarget.GetRootParent() as RootNodeInfo;
                    XmlConnectionNodeSerializer28 connectionNodeSerializer = new(
                        cryptographyProvider,
                        rootNode?.PasswordString
                            .ConvertToSecureString() ??
                        new RootNodeInfo(RootNodeType
                                .Connection)
                            .PasswordString
                            .ConvertToSecureString(),
                        saveFilter);
                    serializer = new XmlConnectionsSerializer(cryptographyProvider, connectionNodeSerializer)
                    {
                        UseFullEncryption = isEncrypted,

                        // An export is the way back. It has to open in upstream mRemoteNG whatever
                        // the store it came from is, so it states its level rather than inheriting
                        // the store's — otherwise hardening the store would silently take the escape
                        // route away with it.
                        StorageFormatOverride = StorageFormatLevel.Classic
                    };
                    break;
                case SaveFormat.mRCSV:
                    serializer =
                        new CsvConnectionsSerializerMremotengFormat(saveFilter, Runtime.CredentialProviderCatalog);
                    break;
                case SaveFormat.mRJSON:
                    serializer = new JsonConnectionsSerializer(saveFilter);
                    break;
                case SaveFormat.RDP:
                    serializer = new RdpConnectionSerializer(saveFilter);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(saveFormat), saveFormat, null);
            }

            var serializedData = serializer.Serialize(exportTarget);
            FileDataProvider fileDataProvider = new(fileName);
            fileDataProvider.Save(serializedData);
        }
        catch (Exception ex)
        {
            Runtime.MessageCollector.AddExceptionStackTrace($"Export.SaveExportFile(\"{fileName}\") failed.", ex);
        }
        finally
        {
            Runtime.ConnectionsService.RemoteConnectionsSyncronizer?.Enable();
        }
    }
}