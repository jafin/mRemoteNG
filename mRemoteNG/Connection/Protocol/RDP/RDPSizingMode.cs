using System.ComponentModel;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;

namespace mRemoteNG.Connection.Protocol.RDP;

public enum RDPSizingMode
{
    [Description("None")]
    None,

    [LocalizedAttributes.LocalizedDescription(nameof(Language.SmartSize))]
    SmartSize,

    [LocalizedAttributes.LocalizedDescription("Smart Size (Aspect Ratio)")]
    SmartSizeAspect
}