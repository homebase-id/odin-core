using System.Collections.Generic;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles;

public class AddCircleMembershipRequest
{
    public string OdinId { get; set; }
    public GuidId CircleId { get; set; }
}