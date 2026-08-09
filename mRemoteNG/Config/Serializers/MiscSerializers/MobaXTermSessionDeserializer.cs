using System;
using System.Globalization;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Serializers.MiscSerializers;

[SupportedOSPlatform("windows")]
public class MobaXTermSessionDeserializer : IDeserializer<string, ConnectionTreeModel>
{
    // MobaXTerm protocol codes
    private const int ProtocolCodeRdp = 91;
    private const int ProtocolCodeSsh = 109;
    private const int ProtocolCodeVnc = 128;
    private const int ProtocolCodeTelnet = 98;
    private const int ProtocolCodeFtp = 130;
    private const int ProtocolCodeSftp = 145;

    public ConnectionTreeModel Deserialize(string mobaFileContent)
    {
        ConnectionTreeModel connectionTreeModel = new();
        RootNodeInfo root = new(RootNodeType.Connection);
        connectionTreeModel.AddRootNode(root);

        if (string.IsNullOrWhiteSpace(mobaFileContent))
            return connectionTreeModel;

        string[] lines = mobaFileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string currentSection = "";
        ContainerInfo? currentContainer = null;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            // Detect section headers: [Bookmarks], [Bookmarks_1], etc.
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1];
                if (currentSection.StartsWith("Bookmarks", StringComparison.OrdinalIgnoreCase))
                {
                    currentContainer = null; // Reset — SubRep will set it
                }
                continue;
            }

            if (!currentSection.StartsWith("Bookmarks", StringComparison.OrdinalIgnoreCase))
                continue;

            // Parse key=value
            int eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
                continue;

            string key = trimmed[..eqIndex].Trim();
            string value = trimmed[(eqIndex + 1)..].Trim();

            if (string.Equals(key, "SubRep", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    currentContainer = new ContainerInfo { Name = value };
                    root.AddChild(currentContainer);
                }
                continue;
            }

            if (string.Equals(key, "ImgNum", StringComparison.OrdinalIgnoreCase))
                continue;

            // Session line: SessionName=#protocolCode#host%port%username%...
            ConnectionInfo? connection = ParseSession(key, value);
            if (connection != null)
            {
                if (currentContainer != null)
                    currentContainer.AddChild(connection);
                else
                    root.AddChild(connection);
            }
        }

        return connectionTreeModel;
    }

    private static ConnectionInfo? ParseSession(string sessionName, string sessionValue)
    {
        // Format: #protocolCode#host%port%username%...
        if (string.IsNullOrEmpty(sessionValue) || !sessionValue.StartsWith('#'))
            return null;

        // Remove leading '#', then split by '#' to get [protocolCode, rest]
        string withoutLeadingHash = sessionValue[1..];
        int secondHash = withoutLeadingHash.IndexOf('#');
        if (secondHash < 0)
            return null;

        string protocolCodeStr = withoutLeadingHash[..secondHash];
        string fieldsStr = withoutLeadingHash[(secondHash + 1)..];

        if (!int.TryParse(protocolCodeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocolCode))
            return null;

        string[] fields = fieldsStr.Split('%');
        string hostname = fields[0];
        string portStr = fields.Length > 1 ? fields[1] : "";
        string username = fields.Length > 2 ? fields[2] : "";

        ProtocolType protocol = MapProtocol(protocolCode);
        int port = GetPort(portStr, protocol);

        ConnectionInfo connection = new()
        {
            Name = sessionName,
            Hostname = hostname,
            Port = port,
            Protocol = protocol,
            Username = username
        };

        return connection;
    }

    private static ProtocolType MapProtocol(int code)
    {
        return code switch
        {
            ProtocolCodeRdp => ProtocolType.RDP,
            ProtocolCodeSsh or ProtocolCodeSftp => ProtocolType.SSH2,
            ProtocolCodeVnc => ProtocolType.VNC,
            ProtocolCodeTelnet => ProtocolType.Telnet,
            ProtocolCodeFtp => ProtocolType.HTTP,
            _ => ProtocolType.RDP
        };
    }

    private static int GetPort(string portStr, ProtocolType protocol)
    {
        if (int.TryParse(portStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0)
            return port;

        return protocol switch
        {
            ProtocolType.RDP => 3389,
            ProtocolType.SSH2 => 22,
            ProtocolType.VNC => 5900,
            ProtocolType.Telnet => 23,
            _ => 0
        };
    }
}