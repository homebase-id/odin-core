using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.Data;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    /// <summary>
    /// Handles reading/writing of app notifications
    /// </summary>
    public abstract class NotificationListDataControllerBase(
        NotificationListService notificationService,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("list")]
        public async Task<AddNotificationResult> AddNotification([FromBody] AddNotificationRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var sender = WebOdinContext.GetCallerOdinIdOrFail();
            return await notificationService.AddNotification(sender, request, WebOdinContext, db);
        }

        [HttpGet("list")]
        public async Task<NotificationsListResult> GetList([FromQuery] int count, [FromQuery] Int64? cursor, [FromQuery] Guid? appId)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await notificationService.GetList(new GetNotificationListRequest()
            {
                AppId = appId,
                Count = count,
                Cursor = cursor == null ? null : new UnixTimeUtcUnique(cursor.Value)
            }, WebOdinContext, db);
        }

        [HttpGet("list/counts-by-appid")]
        public async Task<NotificationsCountResult> GetUnreadCounts()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await notificationService.GetUnreadCounts(WebOdinContext, db);
        }

        [HttpPut("list")]
        public async Task<IActionResult> UpdateNotification([FromBody] UpdateNotificationListRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid request");
            }

            var db = tenantSystemStorage.IdentityDatabase;
            await notificationService.UpdateNotifications(request, WebOdinContext, db);
            return Ok();
        }

        [HttpPost("list/mark-read-by-appid")]
        public async Task<IActionResult> UpdateNotification([FromBody] Guid appId)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            await notificationService.MarkReadByApp(appId, WebOdinContext, db);
            return Ok();
        }

        [HttpDelete("list")]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            await notificationService.Delete(request, WebOdinContext, db);
            return Ok();
        }
    }
}
