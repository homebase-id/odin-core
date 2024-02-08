using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Incoming;
using Odin.Core.Services.Peer.Incoming.Drive;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/app")]
    [AuthorizeValidAppToken]
    public class TransitAppController(TransitInboxProcessor transitInboxProcessor) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<InboxStatus> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            AssertIsValidTargetDriveValue(request.TargetDrive);
            var result = await transitInboxProcessor.ProcessInbox(request.TargetDrive, request.BatchSize);
            return result;
        }
    }
}