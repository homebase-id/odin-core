using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Membership;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;

namespace Odin.Hosting.Controllers.OwnerToken.Membership
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidOwnerToken]
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