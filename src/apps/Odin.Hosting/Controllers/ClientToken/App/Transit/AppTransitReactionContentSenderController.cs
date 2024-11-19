using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerReactionContentV1)]
    [AuthorizeValidAppToken]
    public class AppPeerReactionContentSenderController(
        PeerReactionSenderService peerReactionSenderService)
        : PeerReactionContentSenderControllerBase(peerReactionSenderService);
}