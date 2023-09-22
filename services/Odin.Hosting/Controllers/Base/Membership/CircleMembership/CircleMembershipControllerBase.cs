using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App;

namespace Odin.Hosting.Controllers.Base.Membership.CircleMembership
{
    public class CircleMembershipControllerBase : ControllerBase
    {
        private readonly CircleMembershipService _circleMembershipService;

        public CircleMembershipControllerBase(CircleMembershipService circleMembershipService)
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