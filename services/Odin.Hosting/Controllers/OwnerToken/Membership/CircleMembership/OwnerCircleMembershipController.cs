using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.CircleMembership;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.CircleMembership
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleMembershipController : CircleMembershipControllerBase
    {
        public OwnerCircleMembershipController(CircleMembershipService circleMembershipService) : base(circleMembershipService)
        {
        }
    }
}