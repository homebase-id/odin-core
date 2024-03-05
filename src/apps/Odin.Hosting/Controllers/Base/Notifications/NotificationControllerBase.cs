using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Base;
using Odin.Core.Time;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    /// <summary>
    /// Handles reading/writing of app notifications
    /// </summary>
    public abstract class NotificationControllerBase : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly NotificationListService _notificationService;

        public NotificationControllerBase(NotificationListService notificationService, OdinContextAccessor contextAccessor)
        {
            _notificationService = notificationService;
            _contextAccessor = contextAccessor;
        }

        [HttpPost("list")]
        public async Task<AddNotificationResult> AddNotification([FromBody] AddNotificationRequest request)
        {
            var sender = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
            return await _notificationService.AddNotification(sender, request);
        }

        [HttpGet("list")]
        public async Task<NotificationsListResult> GetList([FromQuery] int count, [FromQuery] Int64? cursor)
        {
            return await _notificationService.GetList(new GetNotificationListRequest()
            {
                Count = count,
                Cursor = cursor == null ? null : new UnixTimeUtcUnique(cursor.Value)
            });
        }

        [HttpPut("list")]
        public async Task<IActionResult> UpdateNotification([FromBody] UpdateNotificationListRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid request");
            }

            await _notificationService.UpdateNotifications(request);
            return Ok();
        }

        [HttpDelete("list")]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            await _notificationService.Delete(request);
            return Ok();
        }
    }
}