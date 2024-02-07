using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.PeerSenderV1)]
    [AuthorizeValidAppToken]
    public class AppTransitSenderController(ITransitService transitService) : TransitSenderControllerBase(transitService);
}