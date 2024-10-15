using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public abstract class PeerProcessControllerBase(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await peerInboxProcessor.ProcessInbox(request.TargetDrive, WebOdinContext, db, request.BatchSize);
            return new JsonResult(result);
        }
    }
}