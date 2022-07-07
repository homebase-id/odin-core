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

        [HttpGet("unblock/{dotYouId}")]
        public async Task<IActionResult> Unblock(string dotYouId)
        {
            var result = await _circleNetwork.Unblock((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("block/{dotYouId}")]
        public async Task<IActionResult> Block(string dotYouId)
        {
            var result = await _circleNetwork.Block((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("disconnect/{dotYouId}")]
        public async Task<IActionResult> Disconnect(string dotYouId)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        //[HttpPost("notify")]
        // public async Task<IActionResult> NotifyConnections(CircleNetworkNotification notification)
        // {
        //     await _circleNetworkNotificationService.NotifyConnections(notification);
        //     return Ok();
        // }

        [HttpGet("status/{dotYouId}")]
        public async Task<IActionResult> GetConnectionInfo(string dotYouId)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity) dotYouId);
            return new JsonResult(result);
        }

        [HttpGet("connected")]
        public async Task<IActionResult> GetConnectedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }

        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlockedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(result);
        }
    }
}