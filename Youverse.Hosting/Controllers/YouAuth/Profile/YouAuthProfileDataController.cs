using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.YouAuth.Profile
{
    [ApiController]
    [Route("/api/youauth/v1/profile")]
    //TODO: add in Authorize tag when merging with seb
    public class YouAuthProfileDataController : Controller
    {
        public YouAuthProfileDataController()
        {
        }


        [HttpGet("{attributeId}")]
        public async Task<IActionResult> Get(Guid attributeId)
        {
            return new JsonResult("");
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            return new JsonResult("");
        }

    }
}