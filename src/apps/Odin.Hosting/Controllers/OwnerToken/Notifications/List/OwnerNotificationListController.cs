using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Hosting.Controllers.Base.Notifications;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications.List
{
    [ApiController]
    [Route(OwnerApiPathConstants.NotificationsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerNotificationListController : NotificationControllerBase
    {
        public OwnerNotificationListController(NotificationListService notificationService, OdinContextAccessor contextAccessor) : base(notificationService,
            contextAccessor)
        {
        }
    }
}