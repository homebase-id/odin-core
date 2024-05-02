using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/inbox/processor")]
    [AuthorizeValidAppToken]
    public class AppTransitProcessController(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage
        ) : TransitProcessControllerBase(peerInboxProcessor, tenantSystemStorage);
}