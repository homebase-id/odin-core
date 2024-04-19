using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.Data;
using Odin.Core.Time;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    /// <summary>
    /// Handles reading/writing of app notifications
    /// </summary>
    public abstract class NotificationListDataControllerBase(NotificationListService notificationService) : OdinControllerBase
    {
        [HttpPost("list")]
        public async Task<AddNotificationResult> AddNotification([FromBody] AddNotificationRequest request)
        {
            var sender = WebOdinContext.GetCallerOdinIdOrFail();
            return await notificationService.AddNotification(sender, request, WebOdinContext);
        }

        [HttpGet("list")]
        public async Task<NotificationsListResult> GetList([FromQuery] int count, [FromQuery] Int64? cursor)
        {
            return await notificationService.GetList(new GetNotificationListRequest()
            {
                Count = count,
                Cursor = cursor == null ? null : new UnixTimeUtcUnique(cursor.Value)
            }, WebOdinContext);
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

        [HttpDelete("list")]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            await notificationService.Delete(request, WebOdinContext);
            return Ok();
        }
    }
}