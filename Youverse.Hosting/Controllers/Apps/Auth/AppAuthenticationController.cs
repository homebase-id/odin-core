using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.AppAuth;

namespace Youverse.Hosting.Controllers.Apps.Auth
{
    [ApiController]
    [Route("/api/apps/v1/auth")]
    public class AppAuthenticationController : Controller
    {
        private readonly IAppAuthenticationService _authService;

        public AppAuthenticationController(IAppAuthenticationService authService)
        {
            _authService = authService;
        }
        
        [HttpPost("exchangeCode")]
        public async Task<IActionResult> ExchangeAuthCode([FromBody]AuthCodeExchangeRequest request)
        {
            var authResult = await _authService.ExchangeAuthCode(request);
            return new JsonResult(authResult);
        }
        
        
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateSessionToken(Guid sessionToken)
        {
            var result = await _authService.ValidateSessionToken(sessionToken);
            return new JsonResult(result);
        }
        
        [HttpPost("expire")]
        public void ExpireSessionToken(Guid sessionToken)
        {
            _authService.ExpireSession(sessionToken);
        }
    }
}