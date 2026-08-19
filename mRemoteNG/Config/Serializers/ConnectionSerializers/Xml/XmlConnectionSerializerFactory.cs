using System.Linq;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;

[SupportedOSPlatform("windows")]
public static class XmlConnectionSerializerFactory
{
    public static ISerializer<ConnectionInfo, string> Build(
        ICryptographyProvider cryptographyProvider,
        ConnectionTreeModel connectionTreeModel,
        SaveFilter? saveFilter = null,
        bool useFullEncryption = false)
    {
        RootNodeInfo rootNode = connectionTreeModel.RootNodes.OfType<RootNodeInfo>().First();
        System.Security.SecureString encryptionKey = rootNode.PasswordString.ConvertToSecureString();

        XmlConnectionNodeSerializer28 connectionNodeSerializer = new(
            cryptographyProvider,
            encryptionKey,
            saveFilter ?? new SaveFilter(),

            // What this write encrypts under, so that a secret nothing ever decrypted can be written
            // back as the bytes it was read as. Describing the key rather than declaring "this is an
            // ordinary save" is deliberate: a rekey, a master-password change, hardening a classic
            // store and an export all come through here, and every one of them produces a different
            // identity on its own without anyone having to remember to say so.
            ConnectionSecretKeyIdentity.For(cryptographyProvider, rootNode.FileKey, rootNode.PasswordString));

        return new XmlConnectionsSerializer(cryptographyProvider, connectionNodeSerializer)
        {
            UseFullEncryption = useFullEncryption
        };
    }
}