#nullable enable

using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstants.PushNotificationsV1)]
    public class AppPushNotificationController(PushNotificationService notificationService, OdinContextAccessor contextAccessor)
        : PushNotificationControllerBase(notificationService, contextAccessor)
    {
    }
}