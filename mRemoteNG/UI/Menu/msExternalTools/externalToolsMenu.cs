using System.ComponentModel;
using System.Windows.Forms;

namespace mRemoteNG.UI.Menu.msExternalTools;

public partial class externalToolsMenu : ToolStripMenuItem
{
    public externalToolsMenu()
    {
        Initialize();
    }

    public externalToolsMenu(IContainer container)
    {
        container.Add(this);

        Initialize();
    }

    private static void Initialize()
    {
    }
}