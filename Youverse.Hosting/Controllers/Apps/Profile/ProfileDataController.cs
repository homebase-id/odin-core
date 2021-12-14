using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Profile
{
    [ApiController]
    [Route("/api/app/v1/profile")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.AppAuthSchemeName)]
    public class ProfileDataController : Controller
    {
        public ProfileDataController()
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