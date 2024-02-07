using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.Peer.ReceivingHost;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public class TransitProcessControllerBase(TransitInboxProcessor transitInboxProcessor) : ControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            if ((request.TargetDrive?.IsValid() ?? false) == false)
            {
                throw new OdinClientException("Invalid target drive", OdinClientErrorCode.InvalidTargetDrive);
            }
            
            var result =  await transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return new JsonResult(result);
        }
    }
}