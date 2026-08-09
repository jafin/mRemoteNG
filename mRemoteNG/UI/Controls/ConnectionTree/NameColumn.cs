using System.Linq;
using System.Runtime.Versioning;
using BrightIdeasSoftware;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;
using mRemoteNG.Tree;

namespace mRemoteNG.UI.Controls.ConnectionTree;

[SupportedOSPlatform("windows")]
public class NameColumn : OLVColumn
{
    public NameColumn(ImageGetterDelegate imageGetterDelegate)
    {
        Text = Language.Name;
        AspectName = "Name";
        // Sized to content but capped (see ConnectionTree.AutoResizeColumn) so it never crowds
        // out the Description column; long names ellipsize with a full-name tooltip. Description
        // keeps FillsFreeSpace and takes whatever the Name column leaves.
        FillsFreeSpace = false;
        AspectGetter = item =>
        {
            var ci = (ConnectionInfo)item;
            var name = ConnectionNameFormatter.FormatName(ci);
            if (ci is ContainerInfo container)
            {
                var count = container.GetRecursiveChildList()
                    .Count(c => c.GetTreeNodeType() == TreeNodeType.Connection ||
                                c.GetTreeNodeType() == TreeNodeType.PuttySession);

                if (count > 0)
                    return $"{name} ({count})";
            }
            return name;
        };
        ImageGetter = imageGetterDelegate;
        AutoCompleteEditor = false;
    }
}