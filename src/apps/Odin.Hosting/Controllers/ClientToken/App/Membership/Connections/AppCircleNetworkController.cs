using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidAppToken]
    public class AppCircleNetworkController : CircleNetworkControllerBase
    {
        public AppCircleNetworkController(CircleNetworkService cn, TenantSystemStorage tenantSystemStorage)
            : base(cn, tenantSystemStorage)
        {
        }
    }
}