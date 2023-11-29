using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.AppNotifications.Data;
using Refit;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    /// <summary>
    /// Handles reading/writing of app notifications
    /// </summary>
    public class NotificationControllerBase : OdinControllerBase
    {
        private readonly NotificationListService _notificationService;

        public NotificationControllerBase(NotificationListService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetList()
        {
            return await _notificationService.GetList();
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetList(Guid id)
        {
            return await _notificationService.Get(id);
        }

        [HttpPost("")]
        public async Task<IActionResult> AddNotification([Body] AddNotificationRequest request)
        {
            await _notificationService.AddNotification(request);
        }

        [HttpPut("")]
        public async Task<IActionResult> UpdateNotification([Body] UpdateNotificationListRequest request)
        {
            await _notificationService.UpdateNotifications(request);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNotification(DeleteNotificationsRequest request)
        {
            await _notificationService.Delete(request);
        }
    }
}