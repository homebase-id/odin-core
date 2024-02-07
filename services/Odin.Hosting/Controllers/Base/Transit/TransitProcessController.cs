using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Incoming;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public class TransitProcessControllerBase(TransitInboxProcessor transitInboxProcessor) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            AssertIsValidTargetDriveValue(request.TargetDrive);
            var result = await transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return new JsonResult(result);
        }
    }
}