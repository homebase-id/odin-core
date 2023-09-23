using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.Peer.ReceivingHost;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public class TransitProcessControllerBase : ControllerBase
    {
        private readonly TransitInboxProcessor _transitInboxProcessor;

        public TransitProcessControllerBase(TransitInboxProcessor transitInboxProcessor)
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