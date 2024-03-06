using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
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
        public AppTransitReactionContentSenderController(PeerReactionSenderService peerReactionSenderService): base(peerReactionSenderService)
        {
        }
    }
}