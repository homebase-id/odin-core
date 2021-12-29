using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Auth
{
    /// <summary>
    /// Note: this is under the /owner path because need to ensure calls
    /// it send the cookie so the <see cref="OwnerAuthenticationHandler"/>
    /// can authenticate the owner before authenticating the app
    /// </summary>
    [ApiController]
    [Route("/owner/api/v1/app/authentication")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.SchemeName)]
    public class AppAuthenticationController : Controller
    {
        private readonly IAppAuthenticationService _authService;

        public AppAuthenticationController(IAppAuthenticationService authService)
        {
            _authService = authService;
        }

        [HttpPost("sessionToken")]
        public async Task<IActionResult> CreateSessionToken([FromBody] AppDevice appDevice)
        {
            var authCode = await _authService.CreateSessionToken(appDevice);
            return new JsonResult(authCode);
        }

        [HttpPost("logout")]
        public void ExpireSessionToken(Guid sessionToken)
        {
            _authService.ExpireSession(sessionToken);
        }

        [HttpPost]
        public async Task<IActionResult> ValidateSessionToken(Guid sessionToken)
        {
            var result = await _authService.ValidateSessionToken(sessionToken);
            return new JsonResult(result);
        }

        [HttpGet("exchangeCode")]
        public async Task<IActionResult> ExchangeAuthCode(Guid authCode)
        {
            var authResult = await _authService.ExchangeAuthCode(authCode);
            return new JsonResult(authResult);
        }
    }
}