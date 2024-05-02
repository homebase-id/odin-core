﻿#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Notifications
{
    [ApiController]
    [AuthorizeValidAppToken]
    [Route(AppApiPathConstants.PushNotificationsV1)]
    public class AppPushNotificationController(
        PushNotificationService notificationService,
        ILoggerFactory loggerFactory,
        TenantSystemStorage tenantSystemStorage)
        : PushNotificationControllerBase(notificationService, loggerFactory, tenantSystemStorage)
    {
    }
}