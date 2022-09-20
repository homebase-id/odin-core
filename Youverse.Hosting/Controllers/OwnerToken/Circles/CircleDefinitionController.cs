using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
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

        [HttpGet("list")]
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions()
        {
            var result = await _cns.GetCircleDefinitions();
            return result;
        }

        [HttpPost("circle")]
        public CircleDefinition GetCircle([FromBody] GuidId id)
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
        public async Task<bool> DeleteCircle([FromBody] byte[] id)
        {
            await _cns.DeleteCircleDefinition(new GuidId(id));
            return true;
        }
        
        [HttpPost("enable")]
        public async Task<bool> EnableCircle([FromBody] byte[] id)
        {
            await _cns.EnableCircle(new GuidId(id));
            return true;
        }
        
        [HttpPost("disable")]
        public async Task<bool> DisableCircle([FromBody] byte[] id)
        {
            await _cns.DisableCircle(new GuidId(id));
            return true;
        }
    }
}