using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace mRemoteNG.UI;

[SupportedOSPlatform("windows")]
public static class FontOverrider
{
    public static void FontOverride(Control ctlParent)
    {
        // Override the font of all controls in a container with the default font based on the OS version
        foreach (Control tempLoopVarCtlChild in ctlParent.Controls)
        {
            var ctlChild = tempLoopVarCtlChild;
            // Only create a new Font if the font name is different to avoid unnecessary GDI operations
            var fontName = SystemFonts.MessageBoxFont?.Name ?? SystemFonts.DefaultFont.Name;
            if (!string.Equals(ctlChild.Font.Name, fontName, StringComparison.Ordinal))
            {
                ctlChild.Font = new Font(fontName, ctlChild.Font.Size, ctlChild.Font.Style,
                    ctlChild.Font.Unit, ctlChild.Font.GdiCharSet);
            }

            if (ctlChild.Controls.Count > 0)
            {
                FontOverride(ctlChild);
            }
        }
    }
}