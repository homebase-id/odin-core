using System.Collections.Generic;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles;

public class AddCircleMembershipRequest
{
    public List<string> DotYouIdList { get; set; }
    public ByteArrayId CircleId { get; set; }
}