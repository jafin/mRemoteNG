using System.Runtime.Versioning;
using mRemoteNG.Container;

namespace mRemoteNG.Tree.Smart;

[SupportedOSPlatform("windows")]
public class SmartGroupRoot : ContainerInfo
{
    public SmartGroupRoot()
    {
        Name = "Smart Groups";
        IsExpanded = true;
        // Ensure this node is not saved to XML by default serializers
        // Serializers usually check for RootNodeInfo or iterate specific roots.
        // This is just a ContainerInfo so it behaves like a folder.
    }

    public override TreeNodeType GetTreeNodeType()
    {
        return TreeNodeType.Container;
    }
}