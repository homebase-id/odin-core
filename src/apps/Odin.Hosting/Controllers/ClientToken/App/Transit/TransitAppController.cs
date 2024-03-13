using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/app")]
    [AuthorizeValidAppToken]
    public class TransitAppController(PeerInboxProcessor peerInboxProcessor) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<InboxStatus> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            var result = await peerInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return result;
        }
    }
}