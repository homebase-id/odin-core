namespace Odin.Hosting.Controllers.Base.Membership.Connections;

public class RevokeCircleMembershipRequest
{
    public string OdinId { get; set; }
    public GuidId CircleId { get; set; }
}