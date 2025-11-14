#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstantsV1.PushNotificationsV1)]
    public class AppPushNotificationController(
        PushNotificationService notificationService,
        ILoggerFactory loggerFactory)
        : PushNotificationControllerBase(notificationService, loggerFactory)
    {
    }
}