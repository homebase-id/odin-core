using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerReactionContentV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerReactionContentSenderController : PeerReactionContentSenderControllerBase
    {
        public OwnerPeerReactionContentSenderController(
            PeerReactionSenderService peerReactionSenderService): base(peerReactionSenderService)
        {
        }
    }
}