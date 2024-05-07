using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleNetworkController : CircleNetworkControllerBase
    {
        public OwnerCircleNetworkController(CircleNetworkService cn, TenantSystemStorage tenantSystemStorage):
            base(cn, tenantSystemStorage)
        {
        }
    }
}