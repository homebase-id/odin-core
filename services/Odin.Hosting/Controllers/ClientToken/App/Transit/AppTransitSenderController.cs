using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.TransitSenderV1)]
    [AuthorizeValidAppToken]
    public class AppTransitSenderController : TransitSenderControllerBase
    {
        public AppTransitSenderController(ITransitService transitService) : base(transitService)
        {
        }
    }
}