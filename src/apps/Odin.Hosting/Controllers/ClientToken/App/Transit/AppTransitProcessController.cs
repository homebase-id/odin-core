using Microsoft.AspNetCore.Mvc;
using Odin.Core.Tasks;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/inbox/processor")]
    [AuthorizeValidAppToken]
    public class AppPeerProcessController(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage,
        IForgottenTasks forgottenTasks
    ) : PeerProcessControllerBase(peerInboxProcessor, tenantSystemStorage, forgottenTasks);
}