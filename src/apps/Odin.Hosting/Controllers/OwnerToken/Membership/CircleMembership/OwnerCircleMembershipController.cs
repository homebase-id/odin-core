using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.CircleMembership;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.CircleMembership
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleMembershipController : CircleMembershipControllerBase
    {
        public OwnerCircleMembershipController(
            CircleMembershipService circleMembershipService) : base(circleMembershipService)
        {
        }
    }
}