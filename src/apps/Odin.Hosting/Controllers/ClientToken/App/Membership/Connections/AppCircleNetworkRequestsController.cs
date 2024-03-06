using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers.Base.Membership.Connections;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidAppToken]
    public class AppCircleNetworkRequestsController : CircleNetworkRequestsControllerBase
    {
        public AppCircleNetworkRequestsController(CircleNetworkRequestService cn) : base(cn)
        {
        }
    }
}