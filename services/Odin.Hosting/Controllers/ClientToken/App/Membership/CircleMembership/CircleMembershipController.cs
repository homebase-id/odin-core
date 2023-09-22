using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.CircleMembership
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidAppToken]
    public class CircleMembershipController : ControllerBase
    {
        private readonly CircleMembershipService _circleMembershipService;

        public CircleMembershipController(CircleMembershipService circleMembershipService)
        {
            _circleMembershipService = circleMembershipService;
        }
        
        [HttpPost("list")]
        public Task<List<CircleDomainResult>> GetDomainsInCircle([FromBody] GetCircleMembersRequest request)
        {
            var result = _circleMembershipService.GetDomainsInCircle(request.CircleId);
            return Task.FromResult(result);
        }
        
    }
}