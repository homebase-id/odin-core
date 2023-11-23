﻿#nullable enable

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerPushNotificationController : Controller
    {
        private readonly PushNotificationService _notificationService;

        public OwnerPushNotificationController(PushNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        /// <summary />
        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeDevice([FromBody] PushNotificationSubscribeDeviceRequest request)
        {
            var sub = new PushNotificationSubscription()
            {
                FriendlyName = request.FriendlyName,
                Endpoint = request.Endpoint,
                // ExpirationTime = ??
                Auth = request.Auth,
                P256DH = request.P256DH
            };

            await _notificationService.AddDevice(sub);

            return Ok();
        }

        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscriptionDetails()
        {
            var subscription = await _notificationService.GetDeviceSubscription();
            if (null == subscription)
            {
                return NotFound();
            }

            return new JsonResult(subscription.Redacted());
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAllSubscriptions()
        {
            var allSubscriptions = await _notificationService.GetAllSubscriptions();
            if (null == allSubscriptions)
            {
                return NotFound();
            }

            return new JsonResult(allSubscriptions.Select(s => s.Redacted()));
        }

        [HttpPost("unsubscribe")]
        public async Task<IActionResult> RemoveDevice()
        {
            await _notificationService.RemoveDevice();
            return Ok();
        }

        [HttpPost("unsubscribeAll")]
        public async Task<IActionResult> RemoveAllDevices()
        {
            await _notificationService.RemoveAllDevices();
            return Ok();
        }

        [HttpPost("push")]
        public async Task<IActionResult> Push([FromBody] PushNotificationContent content)
        {
            await _notificationService.Push(content);

            return Ok();
        }
    }
}