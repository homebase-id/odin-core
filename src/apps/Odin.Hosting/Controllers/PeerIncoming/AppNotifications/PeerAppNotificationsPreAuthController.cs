using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Services.Peer.AppNotification;

namespace Odin.Hosting.Controllers.PeerIncoming.AppNotifications
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.AppNotificationsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerAppNotificationsPreAuthController(PeerAppNotificationService peerAppNotificationService) : OdinControllerBase
    {
        /// <summary />
        [HttpPost("token")]
        public async Task<SharedSecretEncryptedPayload> CreateNotificationToken()
        {
            var result = await peerAppNotificationService.CreateNotificationToken(WebOdinContext);
            return result;
        }
        
        /// <summary />
        [HttpPost("enqueue-push-notification")]
        public async Task<PeerTransferResponse> EnqueuePushNotification([FromBody] PushNotificationOutboxRecord record)
        {
            var result = await peerAppNotificationService.EnqueuePushNotification(record, WebOdinContext);
            return result;
        }
    }
}