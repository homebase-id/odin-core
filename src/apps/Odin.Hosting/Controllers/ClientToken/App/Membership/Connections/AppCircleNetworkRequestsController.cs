using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidAppToken]
    public class AppCircleNetworkRequestsController(
        CircleNetworkRequestService cn,
        CircleNetworkIntroductionService introductionService)
        : CircleNetworkRequestsControllerBase(cn, introductionService);
}