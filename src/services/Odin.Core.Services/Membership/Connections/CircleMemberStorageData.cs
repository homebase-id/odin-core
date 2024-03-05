using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.Connections;

public record CircleMemberStorageData
{
    public AsciiDomainName DomainName { get; set; }
    public CircleGrant CircleGrant { get; set; }
    
    public DomainType DomainType { get; set; }
}