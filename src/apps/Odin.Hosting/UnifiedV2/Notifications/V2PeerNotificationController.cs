using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Peer.AppNotification;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Notifications
{
    /// <summary>
    /// Manages subscriptions to live notifications on drives hosted by other (peer) identities — the
    /// V2 equivalent of <c>/api/apps/v1/notify/peer/{token,subscriptions/push-notification}</c>.
    /// A client first fetches a remote notification token, then subscribes; live events arrive over
    /// the peer websocket (<see cref="V2PeerNotificationSocketController"/>).
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerNotifyRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerNotificationController(PeerAppNotificationService peerAppNotificationService) : OdinControllerBase
    {
        [HttpPost("token")]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<AppNotificationTokenResponse> GetToken([FromBody] GetRemoteTokenRequest request)
        {
            return await peerAppNotificationService.GetRemoteNotificationToken(request, WebOdinContext);
        }

        [HttpPost("subscriptions/push-notification")]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<IActionResult> Subscribe([FromBody] PeerNotificationSubscription request)
        {
            await peerAppNotificationService.SubscribePeerAsync(request, WebOdinContext);
            return NoContent();
        }

        [HttpDelete("subscriptions/push-notification")]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<IActionResult> Unsubscribe([FromBody] PeerNotificationSubscription request)
        {
            await peerAppNotificationService.UnsubscribePeerAsync(request, WebOdinContext);
            return NoContent();
        }

        [HttpGet("subscriptions/push-notification")]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<List<PeerNotificationSubscription>> GetSubscriptions()
        {
            return await peerAppNotificationService.GetAllSubscriptions(WebOdinContext);
        }
    }
}
