using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles;

public class GetCircleMembersRequest
{
    public ByteArrayId CircleId { get; set; }
}