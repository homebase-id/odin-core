using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.CircleMembership;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.CircleMembership
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidAppToken]
    public class AppCircleMembershipController : CircleMembershipControllerBase
    {
        public AppCircleMembershipController(CircleMembershipService circleMembershipService):base(circleMembershipService)
        {
        }
    }
}