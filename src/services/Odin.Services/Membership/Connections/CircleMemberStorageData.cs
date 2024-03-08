using Odin.Core.Util;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Services.Membership.Connections;

public record CircleMemberStorageData
{
    public AsciiDomainName DomainName { get; set; }
    public CircleGrant CircleGrant { get; set; }
    
    public DomainType DomainType { get; set; }
}