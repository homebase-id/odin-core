using System.Collections.Generic;

namespace Odin.Services.Membership.Connections;

public class IcrTroubleshootingInfo
{
    public List<CircleInfo> Circles { get; set; } = new List<CircleInfo>();
}