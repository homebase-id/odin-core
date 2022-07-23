﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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

        [HttpPost("definition")]
        public async Task<IActionResult> GetCircleDefinitions()
        {
            var result = await _circleDefinitionService.GetCircles();
            return new JsonResult(result);
        }

        [HttpPost("circle")]
        public async Task<IActionResult> CreateCircle([FromBody]CreateCircleRequest request)
        {
            await _circleDefinitionService.Create(request);
            return Ok();
        }
        
        [HttpPost("update")]
        public async Task<IActionResult> UpdateCircle(CircleDefinition circleDefinition)
        { 
            await _circleDefinitionService.Update(circleDefinition);
            return Ok();
        }
        
        [HttpPost("delete")]
        public async Task<IActionResult> CreateCircle(Guid id)
        {
            await _circleDefinitionService.Delete(id);
            return Ok();
        }
    }
}