using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerReactionContentV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerTransitReactionContentSenderController : TransitReactionContentSenderControllerBase
    {
        public OwnerTransitReactionContentSenderController(PeerReactionSenderService peerReactionSenderService): base(peerReactionSenderService)
        {
        }
    }
}