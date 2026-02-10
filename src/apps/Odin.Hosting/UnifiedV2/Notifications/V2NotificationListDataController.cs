using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.AppNotifications.Data;

namespace Odin.Hosting.UnifiedV2.Notifications;

[ApiController]
[Route(UnifiedApiRouteConstants.Notify)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2NotificationListDataController(
    NotificationListService notificationService)
    : NotificationListDataControllerBase(notificationService)
{
}