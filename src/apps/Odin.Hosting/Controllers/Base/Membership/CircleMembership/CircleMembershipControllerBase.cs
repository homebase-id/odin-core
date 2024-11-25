using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Membership.CircleMembership
{
    public abstract class CircleMembershipControllerBase : OdinControllerBase
    {
        private readonly CircleMembershipService _circleMembershipService;


        public CircleMembershipControllerBase(CircleMembershipService circleMembershipService)
        {
            _circleMembershipService = circleMembershipService;
            
        }
        
        [HttpPost("list")]
        public async Task<List<CircleDomainResult>> GetDomainsInCircle([FromBody] GetCircleMembersRequest request)
        {
            var result = await _circleMembershipService.GetDomainsInCircleAsync(request.CircleId, WebOdinContext);
            return result;
        }
    }
}