using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Membership.Circles
{
    public abstract class CircleDefinitionControllerBase : ControllerBase
    {
        private readonly CircleNetworkService _cns;
        private readonly CircleMembershipService _circleMembershipService;

        public CircleDefinitionControllerBase(CircleNetworkService cns, CircleMembershipService circleMembershipService)
        {
            _cns = cns;
            _circleMembershipService = circleMembershipService;
        }

        /// <summary>
        /// Returns a list of circle definitions.
        /// </summary>
        /// <param name="includeSystemCircle">if true, the system circle will be included in the results; default is false</param>
        [HttpGet("list")]
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
        {
            var result = await _circleMembershipService.GetCircleDefinitions(includeSystemCircle);
            return result;
        }

        [HttpPost("get")]
        public CircleDefinition GetCircle([FromBody] Guid id)
        {
            return _circleMembershipService.GetCircle(id);
        }

        [HttpPost("create")]
        public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotEmptyGuid(request.Id, nameof(request.Id));
            
            await _circleMembershipService.CreateCircleDefinition(request);
            return true;
        }

        [HttpPost("update")]
        public async Task<bool> UpdateCircle([FromBody] CircleDefinition circleDefinition)
        {
            await _cns.UpdateCircleDefinition(circleDefinition);
            return true;
        }

        [HttpPost("delete")]
        public async Task<bool> DeleteCircle([FromBody] Guid id)
        {
            await _cns.DeleteCircleDefinition(new GuidId(id));
            return true;
        }

        [HttpPost("enable")]
        public async Task<bool> EnableCircle([FromBody] Guid id)
        {
            await _circleMembershipService.EnableCircle(new GuidId(id));
            return true;
        }

        [HttpPost("disable")]
        public async Task<bool> DisableCircle([FromBody] Guid id)
        {
            await _circleMembershipService.DisableCircle(new GuidId(id));
            return true;
        }
    }
}