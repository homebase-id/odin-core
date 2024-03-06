#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Authentication.System;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications.System
{
    [ApiController]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerSystemPushNotificationController : Controller
    {
        private readonly PushNotificationService _notificationService;

        public OwnerSystemPushNotificationController(PushNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        /// <summary />
        [HttpPost("process")]
        public async Task<IActionResult> ProcessBatch()
        {
            await _notificationService.ProcessBatch();
            return Ok();
        }
    }
}