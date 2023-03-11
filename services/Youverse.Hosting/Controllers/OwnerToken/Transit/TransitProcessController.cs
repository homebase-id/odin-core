﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Transit;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/inbox/processor")]
    [AuthorizeValidOwnerToken]
    public class TransitProcessController : ControllerBase
    {
        private readonly ITransitFileReceiverService _transitFileReceiverService;

        public TransitProcessController(ITransitFileReceiverService transitFileReceiverService)
        {
            _transitFileReceiverService = transitFileReceiverService;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessTransitInstructionRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new YouverseClientException("Invalid target drive", YouverseClientErrorCode.InvalidTargetDrive);
            }
            
            await _transitFileReceiverService.ProcessIncomingTransitInstructions(request.TargetDrive);
            return new JsonResult(true);
        }
    }
}