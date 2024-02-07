using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerReactionContentV1)]
    [AuthorizeValidAppToken]
    public class AppTransitReactionContentSenderController : TransitReactionContentSenderControllerBase
    {
        public AppTransitReactionContentSenderController(TransitReactionContentSenderService transitReactionContentSenderService): base(transitReactionContentSenderService)
        {
        }
    }
}