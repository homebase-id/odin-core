using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.Apps;

namespace Youverse.Hosting.Controllers.Apps.Auth
{
    [ApiController]
    [Route(AppApiPathConstants.BasePathV1 + "/auth")]
    public class AppAuthenticationController : Controller
    {
        private readonly IAppAuthenticationService _appAuth;
        
        public AppAuthenticationController(IAppAuthenticationService appAuth)
        {
            _appAuth = appAuth;
        }

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateClientToken(Guid token)
        {
            var result = await _appAuth.ValidateClientToken(token);
            return new JsonResult(result);
        }
    }
}