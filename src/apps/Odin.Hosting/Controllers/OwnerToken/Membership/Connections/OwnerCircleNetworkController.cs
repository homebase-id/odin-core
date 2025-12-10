using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerCircleNetworkController(CircleNetworkService cn, CircleNetworkVerificationService verificationService)
        : CircleNetworkControllerBase(cn, verificationService);
}