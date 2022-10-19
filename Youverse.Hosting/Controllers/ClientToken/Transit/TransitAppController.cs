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

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInstructionRequest request)
        {
            await _transitAppService.ProcessIncomingInstructions(request.TargetDrive);
            return new JsonResult(true);
        }
    }
}