using BrightIdeasSoftware;
using mRemoteNG.Connection;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.UI.Controls.ConnectionTree
{
    [SupportedOSPlatform("windows")]
    public class DescriptionColumn : OLVColumn
    {
        public DescriptionColumn()
        {
            Text = Language.Description;
            AspectName = "Description";
            // Fills whatever horizontal space the (content-sized, capped) Name column leaves.
            FillsFreeSpace = true;
            AspectGetter = item => ((ConnectionInfo)item).Description;
            AutoCompleteEditor = false;
        }
    }
}
