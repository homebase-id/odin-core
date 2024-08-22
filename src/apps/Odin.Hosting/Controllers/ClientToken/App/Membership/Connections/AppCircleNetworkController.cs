using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidAppToken]
    public class AppCircleNetworkController(CircleNetworkService cn, CircleNetworkRequestService requestService, TenantSystemStorage tenantSystemStorage)
        : CircleNetworkControllerBase(cn, tenantSystemStorage, requestService);
}