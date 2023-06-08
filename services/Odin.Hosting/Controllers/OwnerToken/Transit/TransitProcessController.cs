using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.Transit.ReceivingHost;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/inbox/processor")]
    [AuthorizeValidOwnerToken]
    public class TransitProcessController : ControllerBase
    {
        private readonly TransitInboxProcessor _transitInboxProcessor;

        public TransitProcessController(TransitInboxProcessor transitInboxProcessor)
        {
            _transitInboxProcessor = transitInboxProcessor;
        }

        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new OdinClientException("Invalid target drive", OdinClientErrorCode.InvalidTargetDrive);
            }
            
            var result =  await _transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return new JsonResult(result);
        }
    }
}