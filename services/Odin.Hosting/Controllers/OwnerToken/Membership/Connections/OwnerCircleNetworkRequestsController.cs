using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers.Base.Membership.Connections;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleNetworkRequestsController : CircleNetworkRequestsControllerBase
    {
        public OwnerCircleNetworkRequestsController(CircleNetworkRequestService cn):base(cn)
        {
        }
    }
}