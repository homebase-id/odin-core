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
            using var cn = tenantSystemStorage.CreateConnection();
            var sender = WebOdinContext.GetCallerOdinIdOrFail();
            return await notificationService.AddNotification(sender, request, WebOdinContext, cn);
        }

        [HttpGet("list")]
        public async Task<NotificationsListResult> GetList([FromQuery] int count, [FromQuery] Int64? cursor, [FromQuery] Guid? appId)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await notificationService.GetList(new GetNotificationListRequest()
            {
                AppId = appId,
                Count = count,
                Cursor = cursor == null ? null : new UnixTimeUtcUnique(cursor.Value)
            }, WebOdinContext, cn);
        }

        [HttpGet("list/counts-by-appid")]
        public async Task<NotificationsCountResult> GetCountsByApp()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await notificationService.GetUnreadCounts(WebOdinContext, cn);
        }

        [HttpPut("list")]
        public async Task<IActionResult> UpdateNotification([FromBody] UpdateNotificationListRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid request");
            }

            using var cn = tenantSystemStorage.CreateConnection();
            await notificationService.UpdateNotifications(request, WebOdinContext, cn);
            return Ok();
        }

        [HttpPost("list/mark-read-by-appid")]
        public async Task<IActionResult> UpdateNotification(Guid appId)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await notificationService.MarkReadByApp(appId, WebOdinContext, cn);
            return Ok();
        }

        [HttpDelete("list")]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await notificationService.Delete(request, WebOdinContext, cn);
            return Ok();
        }
    }
}