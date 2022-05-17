using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Hosting.Controllers.Apps.Auth
{
    [ApiController]
    [Route(AppApiPathConstants.BasePathV1 + "/auth")]
    public class AppAuthenticationController : Controller
    {
        private readonly ExchangeGrantContextService _exchangeGrantContext;

        public AppAuthenticationController(ExchangeGrantContextService exchangeGrantContext)
        {
            _exchangeGrantContext = exchangeGrantContext;
        }

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateClientToken(string ssCat64)
        {
            var (isValid, _, __) = await _exchangeGrantContext.ValidateClientAuthToken(ssCat64);
            var result = new AppTokenValidationResult()
            {
                IsValid = isValid
            };
            return new JsonResult(result);
        }
    }
}