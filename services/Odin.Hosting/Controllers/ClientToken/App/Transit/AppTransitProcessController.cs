using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1 + "/inbox/processor")]
    [AuthorizeValidAppToken]
    public class AppTransitProcessController : TransitProcessControllerBase
    {
        public AppTransitProcessController(TransitInboxProcessor transitInboxProcessor) : base(transitInboxProcessor)
        {
        }
    }
}