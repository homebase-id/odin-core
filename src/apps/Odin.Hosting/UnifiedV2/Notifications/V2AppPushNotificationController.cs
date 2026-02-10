using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.AppNotifications.Push;

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
    }
}