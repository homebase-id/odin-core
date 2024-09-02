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
        private readonly TenantSystemStorage _tenantSystemStorage;

        public CircleMembershipControllerBase(CircleMembershipService circleMembershipService, TenantSystemStorage tenantSystemStorage)
        {
            _circleMembershipService = circleMembershipService;
            _tenantSystemStorage = tenantSystemStorage;
        }
        
        [HttpPost("list")]
        public Task<List<CircleDomainResult>> GetDomainsInCircle([FromBody] GetCircleMembersRequest request)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = _circleMembershipService.GetDomainsInCircle(request.CircleId, WebOdinContext);
            return Task.FromResult(result);
        }
    }
}