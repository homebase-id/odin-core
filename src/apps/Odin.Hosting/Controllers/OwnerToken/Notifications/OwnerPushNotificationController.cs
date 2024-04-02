﻿#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerPushNotificationController(PushNotificationService notificationService, OdinContextAccessor contextAccessor, ILoggerFactory loggerFactory)
        : PushNotificationControllerBase(notificationService, contextAccessor, loggerFactory)
    {
    }
}