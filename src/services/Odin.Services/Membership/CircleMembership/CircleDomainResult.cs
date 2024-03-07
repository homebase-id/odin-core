using Odin.Core.Util;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Membership.CircleMembership;

public class CircleDomainResult
{
    public AsciiDomainName Domain { get; set; }

    public DomainType DomainType { get; set; }
    
    public RedactedCircleGrant CircleGrant { get; set; }
}