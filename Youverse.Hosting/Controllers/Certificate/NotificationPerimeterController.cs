using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/notification")]
    //TODO: determine if we need an app id here
    // [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetworkWithApp, AuthenticationSchemes = PerimeterAuthConstants.NotificationCertificateAuthScheme)]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.NotificationCertificateAuthScheme)]
    public class NotificationPerimeterController : ControllerBase
    {
        private readonly CircleNetworkNotificationService _systemApiNotificationService;

        public NotificationPerimeterController(CircleNetworkNotificationService systemApiNotificationService)
        {
            _systemApiNotificationService = systemApiNotificationService;
        }

        [HttpPost]
        public async Task<IActionResult> ReceiveNotification([FromBody] SharedSecretEncryptedNotification encryptedNotification)
        {
            await _systemApiNotificationService.ReceiveNotification(encryptedNotification);

            return Ok();
        }
    }
}