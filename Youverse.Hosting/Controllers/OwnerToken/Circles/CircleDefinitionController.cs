using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Definition;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/definitions")]
    [AuthorizeValidOwnerToken]
    public class CircleDefinitionController : ControllerBase
    {
        private readonly CircleDefinitionService _circleDefinitionService;

        public CircleDefinitionController(CircleDefinitionService circleDefinitionService)
        {
            _circleDefinitionService = circleDefinitionService;
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
            await _circleDefinitionService.Update(circleDefinition);
            return true;
        }

        [HttpPost("delete")]
        public async Task<bool> DeleteCircle([FromBody] Guid id)
        {
            await _circleDefinitionService.Delete(id);
            return true;
        }
    }
}