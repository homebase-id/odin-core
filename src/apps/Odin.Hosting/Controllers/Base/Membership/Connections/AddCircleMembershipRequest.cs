using Odin.Core;

namespace Odin.Hosting.Controllers.Base.Membership.Connections;

public class AddCircleMembershipRequest
{
    public string OdinId { get; set; }
    public GuidId CircleId { get; set; }
}