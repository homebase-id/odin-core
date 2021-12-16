using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Profile;

namespace Youverse.Hosting.Controllers.YouAuth.Profile
{
    [ApiController]
    [Route("/api/youauth/v1/profile")]
    //TODO: add in Authorize tag when merging with seb
    public class YouAuthProfileDataController : Controller
    {
        private readonly IOwnerDataAttributeReaderService _attributeReaderService;
        public YouAuthProfileDataController(IOwnerDataAttributeReaderService attributeReaderService)
        {
            _attributeReaderService = attributeReaderService;
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var attributes = await _attributeReaderService.GetAttributes(new PageOptions(pageNumber, pageSize));
            
            //Note: using object because: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-polymorphism
            var collection = attributes.Results.Cast<object>();
            return new JsonResult(collection);
        }
    }
}