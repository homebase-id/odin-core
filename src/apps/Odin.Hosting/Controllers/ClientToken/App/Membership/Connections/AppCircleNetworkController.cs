using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidAppToken]
    public class AppCircleNetworkController : CircleNetworkControllerBase
    {
        public AppCircleNetworkController(CircleNetworkService cn) : base(cn)
        {
        }
    }
}