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
        private readonly TransitInboxProcessor _transitInboxProcessor;

        public TransitAppController(TransitInboxProcessor transitInboxProcessor)
        {
            _transitInboxProcessor = transitInboxProcessor;
        }

        [HttpPost("process")]
        public async Task<InboxStatus> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new YouverseClientException("Invalid target drive", YouverseClientErrorCode.InvalidTargetDrive);
            }

            var result = await _transitInboxProcessor.ProcessInbox(request.TargetDrive);
            return result;
        }
    }
}