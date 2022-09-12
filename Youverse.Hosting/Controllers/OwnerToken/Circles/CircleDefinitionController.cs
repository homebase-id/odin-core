using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Core.Services.Contacts.Circle.Membership;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/definitions")]
    [AuthorizeValidOwnerToken]
    public class CircleDefinitionController : ControllerBase
    {
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly ICircleNetworkService _cns;

        public CircleDefinitionController(CircleDefinitionService circleDefinitionService, ICircleNetworkService cns)
        {
            _circleDefinitionService = circleDefinitionService;
            _cns = cns;
        }

        [HttpGet("list")]
        public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions()
        {
            var result = await _circleDefinitionService.GetCircles();
            return result;
        }
        
        [HttpPost("circle")]
        public CircleDefinition GetCircle([FromBody] ByteArrayId id)
        {
            return _circleDefinitionService.GetCircle(id);
        }

        [HttpPost("create")]
        public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
        {
            await _circleDefinitionService.Create(request);
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
            //TODO: not too much a fan of this being in a controller but breaking out a whole other class for this requires more need 
            var canDelete = await _cns.CanDeleteCircle(new ByteArrayId(id.ToByteArray()));
            if (!canDelete)
            {
                throw new YouverseException("Cannot delete a circle with members");
            }
            
            await _circleDefinitionService.Delete(id);
            return true;
        }
    }
}