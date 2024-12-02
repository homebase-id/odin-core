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

            await _circleMembershipService.CreateCircleDefinitionAsync(request, WebOdinContext);
            return true;
        }

        [HttpPost("update")]
        public async Task<bool> UpdateCircle([FromBody] CircleDefinition circleDefinition)
        {
            await _cns.UpdateCircleDefinitionAsync(circleDefinition, WebOdinContext);
            return true;
        }

        [HttpPost("delete")]
        public async Task<bool> DeleteCircle([FromBody] Guid id)
        {
            await _cns.DeleteCircleDefinitionAsync(new GuidId(id), WebOdinContext);
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