using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Apps.Auth
{
    [ApiController]
    [Route("/api/apps/v1/auth")]
    public class AppAuthenticationController : Controller
    {
        private readonly IOwnerAuthenticationService _authService;
        private readonly IOwnerSecretService _ss;

        public AppAuthenticationController(IOwnerAuthenticationService authService, IOwnerSecretService ss)
        {
            _authService = authService;
            _ss = ss;
        }

        [HttpGet("verifyDeviceToken")]
        public async Task<IActionResult> VerifyDeviceToken()
        {
            //note: this will intentionally ignore any error, including token parsing errors
            var value = Request.Cookies[AppAuthConstants.CookieName];
            var result = DotYouAuthenticationResult.Parse(value);
            var isValid = await _authService.IsValidDeviceToken(result.SessionToken);
            return new JsonResult(isValid);
        }

        [HttpPost("expire")]
        public Task<JsonResult> ExpireToken()
        {
            var value = Request.Cookies[AppAuthConstants.CookieName];
            var result = DotYouAuthenticationResult.Parse(value);
            _authService.ExpireToken(result.SessionToken);

            Response.Cookies.Delete(OwnerAuthConstants.CookieName);

            return Task.FromResult(new JsonResult(true));
        }

        [HttpPost("extend")]
        public async Task<IActionResult> Extend()
        {
            var value = Request.Cookies[AppAuthConstants.CookieName];
            var result = DotYouAuthenticationResult.Parse(value);
            await _authService.ExtendTokenLife(result.SessionToken, 100);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}