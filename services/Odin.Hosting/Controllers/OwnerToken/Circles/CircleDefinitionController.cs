using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Contacts.Circle.Membership.Definition;

namespace Odin.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/definitions")]
    [AuthorizeValidOwnerToken]
    public class CircleDefinitionController : ControllerBase
    {
        private readonly ICircleNetworkService _cns;

        public CircleDefinitionController(ICircleNetworkService cns)
        {
            _cns = cns;
        }

        /// <summary>
        /// Returns a list of circle definitions.
        /// </summary>
        /// <param name="includeSystemCircle">if true, the system circle will be included in the results; default is false</param>
        [HttpGet("list")]
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
        {
            var result = await _cns.GetCircleDefinitions(includeSystemCircle);
            return result;
        }

        [HttpPost("get")]
        public CircleDefinition GetCircle([FromBody] Guid id)
        {
            return _cns.GetCircleDefinition(id);
        }

        [HttpPost("create")]
        public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
        {
            await _cns.CreateCircleDefinition(request);
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
            await _cns.EnableCircle(new GuidId(id));
            return true;
        }

        [HttpPost("disable")]
        public async Task<bool> DisableCircle([FromBody] Guid id)
        {
            await _cns.DisableCircle(new GuidId(id));
            return true;
        }
    }
}