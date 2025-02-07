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
        NotificationListService notificationService) : OdinControllerBase
    {
        [HttpPost("list")]
        public async Task<AddNotificationResult> AddNotification([FromBody] AddNotificationRequest request)
        {
            var sender = WebOdinContext.GetCallerOdinIdOrFail();
            return await notificationService.AddNotification(sender, request, WebOdinContext);
        }

        [HttpGet("list")]
        public async Task<NotificationsListResult> GetList([FromQuery] int count, [FromQuery] string cursor, [FromQuery] Guid? appId)
        {
            return await notificationService.GetList(new GetNotificationListRequest()
            {
                AppId = appId,
                Count = count,
                Cursor = cursor
            }, WebOdinContext);
        }

        [HttpGet("list/counts-by-appid")]
        public async Task<NotificationsCountResult> GetUnreadCounts()
        {
            return await notificationService.GetUnreadCounts(WebOdinContext);
        }

        [HttpPut("list")]
        public async Task<IActionResult> UpdateNotification([FromBody] UpdateNotificationListRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid request");
            }

            await notificationService.UpdateNotifications(request, WebOdinContext);
            return Ok();
        }

        [HttpPost("list/mark-read-by-appid")]
        public async Task<IActionResult> UpdateNotification([FromBody] Guid appId)
        {
            await notificationService.MarkReadByApp(appId, WebOdinContext);
            return Ok();
        }

        [HttpPost("list/mark-read-by-appid-and-typeid")]
        public async Task<IActionResult> UpdateNotificationByTypeId([FromBody] MarkNotificationsAsReadRequest request)
        {
            await notificationService.MarkReadByAppAndTypeId(request.AppId, request.TypeId, WebOdinContext);
            return Ok();
        }

        [HttpDelete("list")]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            await notificationService.Delete(request, WebOdinContext);
            return Ok();
        }
    }
}