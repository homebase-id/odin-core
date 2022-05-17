using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Hosting.Controllers.Owner.Security
{
    [ApiController]
    [Route(OwnerApiPathConstants.SecurityConfig)]
    [AuthorizeOwnerConsole]
    public class SecurityConfigController : Controller
    {
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IAppRegistrationService _appRegistrationService;

        public SecurityConfigController(IAppRegistrationService appRegistrationService, ExchangeGrantService exchangeGrantService)
        {
            _exchangeGrantService = exchangeGrantService;
            _appRegistrationService = appRegistrationService;
        }

        [HttpGet("identitygrants")]
        public async Task<IActionResult> GetIdentityExchangeGrants(int pageNumber, int pageSize)
        {
            var grantList = await _exchangeGrantService.GetIdentityExchangeGrantList(new PageOptions(pageNumber, pageSize));
            return new JsonResult(grantList);
        }
        
        [HttpGet("appgrants")]
        public async Task<IActionResult> GetAppExchangeGrants(int pageNumber, int pageSize)
        {
            var grantList = await _exchangeGrantService.GetAppExchangeGrantList(new PageOptions(pageNumber, pageSize));
            return new JsonResult(grantList);
        }

    }
}