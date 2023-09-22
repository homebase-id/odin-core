using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/definitions")]
    [AuthorizeValidAppToken]
    public class CircleDefinitionController : ControllerBase
    {
        private readonly CircleMembershipService _circleMembershipService;

        public CircleDefinitionController(CircleMembershipService circleMembershipService)
        {
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
    }
}