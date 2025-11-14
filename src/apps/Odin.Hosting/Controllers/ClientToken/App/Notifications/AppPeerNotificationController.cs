using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstantsV1.PeerNotificationsV1)]
    [AuthorizeValidAppToken]
    public class AppPeerNotificationController(
        PeerAppNotificationService peerAppNotificationService) :
        OdinControllerBase
    {
        [HttpPost("token")]
        public async Task<AppNotificationTokenResponse> GetToken([FromBody] GetRemoteTokenRequest request)
        {
            return await peerAppNotificationService.GetRemoteNotificationToken(request, WebOdinContext);
        }

        [HttpPost("subscriptions/push-notification")]
        public async Task<IActionResult> Subscribe([FromBody] PeerNotificationSubscription request)
        {
            await peerAppNotificationService.SubscribePeerAsync(request, WebOdinContext);
            return NoContent();
        }

        [HttpDelete("subscriptions/push-notification")]
        public async Task<IActionResult> Unsubscribe([FromBody] PeerNotificationSubscription request)
        {
            await peerAppNotificationService.UnsubscribePeerAsync(request, WebOdinContext);
            return NoContent();
        }

        [HttpGet("subscriptions/push-notification")]
        public async Task<List<PeerNotificationSubscription>> GetSubscriptions()
        {
            var list = await peerAppNotificationService.GetAllSubscriptions(WebOdinContext);
            return list;
        }
    }
}