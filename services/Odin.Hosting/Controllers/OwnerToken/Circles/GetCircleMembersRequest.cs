using Odin.Core;

namespace Odin.Hosting.Controllers.OwnerToken.Circles;

public class GetCircleMembersRequest
{
    public GuidId CircleId { get; set; }
}