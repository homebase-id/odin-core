using System.Collections.Generic;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles;

public class RevokeCircleMembershipRequest
{
    public string DotYouId { get; set; }
    public ByteArrayId CircleId { get; set; }
}