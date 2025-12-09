using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerCircleNetworkRequestsController(
        CircleNetworkRequestService cn,
        CircleNetworkIntroductionService introductionService)
        : CircleNetworkRequestsControllerBase(cn, introductionService);
}