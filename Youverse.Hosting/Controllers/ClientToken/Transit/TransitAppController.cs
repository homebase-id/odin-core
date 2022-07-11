using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.ClientToken.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/app")]
    [AuthorizeValidAppExchangeGrant]
    public class TransitAppController : ControllerBase
    {
        private readonly ITransitAppService _transitAppService;

        public TransitAppController(ITransitAppService transitAppService)
        {
            _transitAppService = transitAppService;
        }

        [Obsolete("TODO: replace with new outbox process")]
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers()
        {
            await _transitAppService.ProcessTransfers();
            return new JsonResult(true);
        }
        
    }
}