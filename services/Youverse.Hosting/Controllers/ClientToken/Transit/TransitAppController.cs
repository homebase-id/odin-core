using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.ReceivingHost;

namespace Youverse.Hosting.Controllers.ClientToken.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/app")]
    [AuthorizeValidAppExchangeGrant]
    public class TransitAppController : ControllerBase
    {
        private readonly ITransitInboxProcessor _transitInboxProcessor;

        public TransitAppController(ITransitInboxProcessor transitInboxProcessor)
        {
            _transitInboxProcessor = transitInboxProcessor;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessTransitInstructionRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new YouverseClientException("Invalid target drive", YouverseClientErrorCode.InvalidTargetDrive);
            }

            await _transitInboxProcessor.ProcessIncomingTransitInstructions(request.TargetDrive);
            return new JsonResult(true);
        }
    }
}