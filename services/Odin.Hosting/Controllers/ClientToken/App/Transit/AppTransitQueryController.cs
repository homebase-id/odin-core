﻿using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.TransitQueryV1)]
    [AuthorizeValidAppToken]
    public class AppTransitQueryController : TransitQueryControllerBase
    {
        public AppTransitQueryController(TransitQueryService transitQueryService):base(transitQueryService)
        {
        }
    }
}
