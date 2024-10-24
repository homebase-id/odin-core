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
    [Route(AppApiPathConstants.PeerNotificationsV1)]
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
    }
}