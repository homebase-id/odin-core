using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Membership.Circles
{
    public abstract class CircleDefinitionControllerBase : OdinControllerBase
    {
        private readonly CircleNetworkService _dbs;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public CircleDefinitionControllerBase(CircleNetworkService dbs, CircleMembershipService circleMembershipService, TenantSystemStorage tenantSystemStorage)
        {
            _dbs = dbs;
            _circleMembershipService = circleMembershipService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Returns a list of circle definitions.
        /// </summary>
        /// <param name="includeSystemCircle">if true, the system circle will be included in the results; default is false</param>
        [HttpGet("list")]
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
        {
            var result = await _circleMembershipService.GetCircleDefinitions(includeSystemCircle, WebOdinContext);
            return result;
        }

        [HttpPost("get")]
        public async Task<CircleDefinition> GetCircle([FromBody] Guid id)
        {
            return await _circleMembershipService.GetCircleAsync(id, WebOdinContext);
        }

        [HttpPost("create")]
        public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotEmptyGuid(request.Id, nameof(request.Id));

            await _circleMembershipService.CreateCircleDefinition(request, WebOdinContext);
            return true;
        }

        [HttpPost("update")]
        public async Task<bool> UpdateCircle([FromBody] CircleDefinition circleDefinition)
        {
            await _dbs.UpdateCircleDefinitionAsync(circleDefinition, WebOdinContext);
            return true;
        }

        [HttpPost("delete")]
        public async Task<bool> DeleteCircle([FromBody] Guid id)
        {
            await _dbs.DeleteCircleDefinitionAsync(new GuidId(id), WebOdinContext);
            return true;
        }

        [HttpPost("enable")]
        public async Task<bool> EnableCircle([FromBody] Guid id)
        {
            await _circleMembershipService.EnableCircleAsync(new GuidId(id), WebOdinContext);
            return true;
        }

        [HttpPost("disable")]
        public async Task<bool> DisableCircle([FromBody] Guid id)
        {
            await _circleMembershipService.DisableCircleAsync(new GuidId(id), WebOdinContext);
            return true;
        }
    }
}