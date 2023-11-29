using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Time;
using Refit;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    /// <summary>
    /// Handles reading/writing of app notifications
    /// </summary>
    public class NotificationControllerBase : OdinControllerBase
    {
        private readonly NotificationDataService _notificationService;

        public NotificationControllerBase(NotificationDataService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost]
        public async Task<IActionResult> AddNotification([FromBody] AddNotificationRequest request)
        {
            await _notificationService.AddNotification(request);
            return Ok();
        }

        [HttpGet]
        public async Task<NotificationsListResult> GetList([Query] int count, [Query] UnixTimeUtcUnique cursor)
        {
            return await _notificationService.GetList(new GetNotificationListRequest()
            {
                Count = count,
                Cursor = cursor
            });
        }

        [HttpPut]
        public async Task<IActionResult> UpdateNotification([FromBody] UpdateNotificationListRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid request");
            }

            await _notificationService.UpdateNotifications(request);
            return Ok();
        }

        [HttpDelete()]
        public async Task<IActionResult> DeleteNotification([FromBody] DeleteNotificationsRequest request)
        {
            await _notificationService.Delete(request);
            return Ok();
        }
    }
}