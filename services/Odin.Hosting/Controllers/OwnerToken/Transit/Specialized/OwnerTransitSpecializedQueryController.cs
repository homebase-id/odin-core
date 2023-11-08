using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit.Specialized
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerTransitSpecializedQueryController : TransitQueryControllerBase
    {
        public OwnerTransitSpecializedQueryController(TransitQueryService transitQueryService) : base(transitQueryService)
        {
        }
    }
}