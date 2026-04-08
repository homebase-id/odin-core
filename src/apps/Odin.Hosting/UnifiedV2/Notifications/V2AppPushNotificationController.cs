using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Notifications
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.Notify)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2AppPushNotificationController(
        PushNotificationService notificationService,
        ILoggerFactory loggerFactory)
        : PushNotificationControllerBase(notificationService, loggerFactory)
    {
        /// <summary>
        /// Returns the current device's push notification subscription including the Firebase device token.
        /// Returns 404 if no subscription exists; use this to verify whether a push notification token is registered.
        /// </summary>
        [HttpGet("subscription")]
        public override async Task<IActionResult> GetSubscriptionDetails()
        {
            var subscription = await NotificationService.GetDeviceSubscriptionAsync(WebOdinContext);
            if (subscription == null)
            {
                return NotFound();
            }

            return new JsonResult(subscription.RedactedV2());
        }
    }
}