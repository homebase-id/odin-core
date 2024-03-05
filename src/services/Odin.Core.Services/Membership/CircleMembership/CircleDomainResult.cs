using Odin.Core.Services.Membership.Connections;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.CircleMembership;

public class CircleDomainResult
{
    public AsciiDomainName Domain { get; set; }

    public DomainType DomainType { get; set; }
    
    public RedactedCircleGrant CircleGrant { get; set; }
}