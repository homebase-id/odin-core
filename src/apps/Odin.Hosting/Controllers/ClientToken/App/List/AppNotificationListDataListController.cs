using Microsoft.AspNetCore.Mvc;
using Odin.Services.AppNotifications.Data;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.List
{
    [ApiController]
    [Route(AppApiPathConstantsV1.NotificationsV1)]
    [AuthorizeValidAppToken]
    public class AppNotificationListDataListController : NotificationListDataControllerBase
    {
        public AppNotificationListDataListController(
            NotificationListService notificationService) : base(notificationService)
        {
        }
    }
}