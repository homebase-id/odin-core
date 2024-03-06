using Microsoft.AspNetCore.Mvc;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Base;
using Odin.Hosting.Controllers.Base.Notifications;

namespace Odin.Hosting.Controllers.ClientToken.App.List
{
    [ApiController]
    [Route(AppApiPathConstants.NotificationsV1)]
    [AuthorizeValidAppToken]
    public class AppNotificationListController : NotificationControllerBase
    {
        public AppNotificationListController(NotificationListService notificationService, OdinContextAccessor contextAccessor) : base(notificationService,
            contextAccessor)
        {
        }
    }
}