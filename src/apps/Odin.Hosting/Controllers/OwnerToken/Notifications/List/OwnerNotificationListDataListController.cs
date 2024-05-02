﻿using Microsoft.AspNetCore.Mvc;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Controllers.Base.Notifications;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications.List
{
    [ApiController]
    [Route(OwnerApiPathConstants.NotificationsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerNotificationListDataListController : NotificationListDataControllerBase
    {
        public OwnerNotificationListDataListController(
            NotificationListService notificationService,
            TenantSystemStorage tenantSystemStorage) : base(notificationService, tenantSystemStorage)
        {
        }
    }
}