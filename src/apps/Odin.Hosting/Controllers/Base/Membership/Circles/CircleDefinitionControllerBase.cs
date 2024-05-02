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
        private readonly CircleNetworkService _cns;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public CircleDefinitionControllerBase(CircleNetworkService cns, CircleMembershipService circleMembershipService, TenantSystemStorage tenantSystemStorage)
        {
            _cns = cns;
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
            using var cn = _tenantSystemStorage.CreateConnection();
            var result = await _circleMembershipService.GetCircleDefinitions(includeSystemCircle, WebOdinContext, cn);
            return result;
        }

        [HttpPost("get")]
        public CircleDefinition GetCircle([FromBody] Guid id)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            return _circleMembershipService.GetCircle(id, WebOdinContext, cn);
        }

        [HttpPost("create")]
        public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotEmptyGuid(request.Id, nameof(request.Id));

            using var cn = _tenantSystemStorage.CreateConnection();
            await _circleMembershipService.CreateCircleDefinition(request, WebOdinContext, cn);
            return true;
        }

        [HttpPost("update")]
        public async Task<bool> UpdateCircle([FromBody] CircleDefinition circleDefinition)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _cns.UpdateCircleDefinition(circleDefinition, WebOdinContext, cn);
            return true;
        }

        [HttpPost("delete")]
        public async Task<bool> DeleteCircle([FromBody] Guid id)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _cns.DeleteCircleDefinition(new GuidId(id), WebOdinContext, cn);
            return true;
        }

        [HttpPost("enable")]
        public async Task<bool> EnableCircle([FromBody] Guid id)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _circleMembershipService.EnableCircle(new GuidId(id), WebOdinContext, cn);
            return true;
        }

        [HttpPost("disable")]
        public async Task<bool> DisableCircle([FromBody] Guid id)
        {
            using var cn = _tenantSystemStorage.CreateConnection();
            await _circleMembershipService.DisableCircle(new GuidId(id), WebOdinContext, cn);
            return true;
        }
    }
}