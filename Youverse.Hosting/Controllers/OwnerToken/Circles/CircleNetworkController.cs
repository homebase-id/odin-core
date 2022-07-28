using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Notification;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class CircleNetworkController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;
        private readonly CircleNetworkNotificationService _circleNetworkNotificationService;

        public CircleNetworkController(ICircleNetworkService cn, CircleNetworkNotificationService circleNetworkNotificationService)
        {
            _circleNetwork = cn;
            _circleNetworkNotificationService = circleNetworkNotificationService;
        }

        [HttpPost("unblock")]
        public async Task<IActionResult> Unblock([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Unblock((DotYouIdentity)request.DotYouId);
            return new JsonResult(result);
        }

        [HttpPost("block")]
        public async Task<IActionResult> Block([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Block((DotYouIdentity)request.DotYouId);
            return new JsonResult(result);
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity)request.DotYouId);
            return new JsonResult(result);
        }

        //[HttpPost("notify")]
        // public async Task<IActionResult> NotifyConnections(CircleNetworkNotification notification)
        // {
        //     await _circleNetworkNotificationService.NotifyConnections(notification);
        //     return Ok();
        // }

        [HttpPost("status")]
        public async Task<IActionResult> GetConnectionInfo([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity)request.DotYouId);
            return new JsonResult(result);
        }

        [HttpPost("connected")]
        public async Task<IActionResult> GetConnectedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }

        [HttpPost("blocked")]
        public async Task<IActionResult> GetBlockedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }
    }
}