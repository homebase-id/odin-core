using Odin.Core;

namespace Odin.Hosting.Controllers.OwnerToken.Circles;

public class RevokeCircleMembershipRequest
{
    public string OdinId { get; set; }
    public GuidId CircleId { get; set; }
}