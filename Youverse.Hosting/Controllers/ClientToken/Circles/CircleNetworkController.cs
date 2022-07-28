using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.ClientToken.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [Route(YouAuthApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidAppExchangeGrant]
    public class CircleNetworkController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;

        public CircleNetworkController(ICircleNetworkService cn)
        {
            _circleNetwork = cn;
        }

        [HttpPost("status")]
        public async Task<IActionResult> GetConnectionInfo(DotYouIdRequest request)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity)request.DotYouId);

            return new JsonResult(new ConnectionInfoResponse()
            {
                Status = result.Status,
                LastUpdated = result.LastUpdated,
                GrantIsRevoked = result.AccessGrant.Grant.IsRevoked || result.AccessGrant.AccessRegistration.IsRevoked
            });
        }

        [HttpGet("connected")]
        public async Task<IActionResult> GetConnectedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }
    }
}