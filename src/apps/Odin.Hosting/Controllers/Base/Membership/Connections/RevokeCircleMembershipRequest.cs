using Odin.Core;
using Odin.Core.Identity;

namespace Odin.Hosting.Controllers.Base.Membership.Connections;

public class RevokeCircleMembershipRequest
{
    public OdinId OdinId { get; set; }
    public GuidId CircleId { get; set; }
}