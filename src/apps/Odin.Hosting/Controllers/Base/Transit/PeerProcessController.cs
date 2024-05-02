using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public class TransitProcessControllerBase(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await peerInboxProcessor.ProcessInbox(request.TargetDrive, WebOdinContext, cn, request.BatchSize);
            return new JsonResult(result);
        }
    }
}