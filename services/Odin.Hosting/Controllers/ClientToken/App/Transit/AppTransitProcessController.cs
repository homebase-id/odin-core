using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Incoming;
using Odin.Core.Services.Peer.Incoming.Drive;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerV1 + "/inbox/processor")]
    [AuthorizeValidAppToken]
    public class AppTransitProcessController(TransitInboxProcessor transitInboxProcessor) : TransitProcessControllerBase(transitInboxProcessor);
}