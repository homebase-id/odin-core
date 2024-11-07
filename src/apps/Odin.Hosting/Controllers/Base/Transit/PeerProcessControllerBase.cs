using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public abstract class PeerProcessControllerBase(
        PeerInboxProcessor peerInboxProcessor) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            var result = await peerInboxProcessor.ProcessInboxAsync(request.TargetDrive, WebOdinContext, request.BatchSize);
            return new JsonResult(result);
        }
    }
}