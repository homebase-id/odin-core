using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
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
        public async Task<bool> Unblock([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Unblock((DotYouIdentity)request.DotYouId);
            return result;
        }

        [HttpPost("block")]
        public async Task<bool> Block([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Block((DotYouIdentity)request.DotYouId);
            return result;
        }

        [HttpPost("disconnect")]
        public async Task<bool> Disconnect([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity)request.DotYouId);
            return result;
        }

        //[HttpPost("notify")]
        // public async Task<IActionResult> NotifyConnections(CircleNetworkNotification notification)
        // {
        //     await _circleNetworkNotificationService.NotifyConnections(notification);
        //     return Ok();
        // }

        [HttpPost("status")]
        public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity)request.DotYouId);
            return result?.Redacted();
        }
        
        // [HttpPost("status")]
        // public async Task<IdentityConnectionRegistration> GetConnectionInfo([FromBody] DotYouIdRequest request)
        // {
        //     var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity)request.DotYouId);
        //     return result;
        // }

        [HttpPost("connected")]
        public async Task<PagedResult<DotYouProfile>> GetConnectedIdentities(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetConnectedIdentities(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpPost("blocked")]
        public async Task<PagedResult<DotYouProfile>> GetBlockedProfiles(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return result;
        }
    }
}