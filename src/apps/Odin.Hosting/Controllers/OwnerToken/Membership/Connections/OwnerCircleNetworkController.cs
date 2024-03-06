using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleNetworkController : CircleNetworkControllerBase
    {
        public OwnerCircleNetworkController(CircleNetworkService cn):base(cn)
        {
        }
    }
}