using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes reaction requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitReactionContentV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerTransitReactionContentSenderController : TransitReactionContentSenderControllerBase
    {
        public OwnerTransitReactionContentSenderController(TransitReactionContentSenderService transitReactionContentSenderService): base(transitReactionContentSenderService)
        {
        }
    }
}