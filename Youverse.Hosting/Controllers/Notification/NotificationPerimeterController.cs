using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Notification;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.Notification
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/notification")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetworkWithApp, AuthenticationSchemes = PerimeterAuthConstants.NotificationCertificateAuthScheme)]
    public class NotificationPerimeterController : ControllerBase
    {
        public NotificationPerimeterController()
        {
        }
        
        [HttpPost]
        public async Task<IActionResult> ReceiveNotification([FromBody] SharedSecretEncryptedNotification encryptedNotification)
        {
            var iv = encryptedNotification.InitializationVector;
            //shared secret comes from the icr?

            return Ok();

        }

    }
}