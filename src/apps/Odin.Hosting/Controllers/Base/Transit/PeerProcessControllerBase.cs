using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Tasks;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Transit
{
    public abstract class PeerProcessControllerBase(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage,
        IForgottenTasks forgottenTasks) : OdinControllerBase
    {
        [HttpPost("process")]
        public Task<IActionResult> ProcessTransfers([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            using var cn = tenantSystemStorage.CreateConnection();
            var task = peerInboxProcessor.ProcessInbox(request.TargetDrive, WebOdinContext, cn, request.BatchSize);
            forgottenTasks.Add(task);
            return Task.FromResult<IActionResult>(new OkResult());
        }

        [HttpPost("process-sync")]
        public async Task<IActionResult> ProcessTransfersSync([FromBody] ProcessInboxRequest request)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await peerInboxProcessor.ProcessInbox(request.TargetDrive, WebOdinContext, cn, request.BatchSize);
            return new JsonResult(result);
        }
    }
}