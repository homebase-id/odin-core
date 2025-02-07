#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Controllers.Base.Notifications
{
    public abstract class PushNotificationControllerBase(
        PushNotificationService notificationService,
        ILoggerFactory loggerFactory)
        : OdinControllerBase
    {
        private readonly ILogger<PushNotificationControllerBase> _logger =
            loggerFactory.CreateLogger<PushNotificationControllerBase>();

        /// <summary />
        [HttpPost("subscribe")]
        public async Task<IActionResult> SubscribeDevice([FromBody] PushNotificationSubscribeDeviceRequest request)
        {
            var subscription = new PushNotificationSubscription()
            {
                FriendlyName = request.FriendlyName,
                Endpoint = request.Endpoint,
                // ExpirationTime = ??
                Auth = request.Auth,
                P256DH = request.P256DH
            };

            if (subscription == null ||
                string.IsNullOrEmpty(subscription.Endpoint) || string.IsNullOrWhiteSpace(subscription.Endpoint) ||
                string.IsNullOrEmpty(subscription.Auth) || string.IsNullOrWhiteSpace(subscription.Auth) ||
                string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrWhiteSpace(subscription.P256DH))
            {
                throw new OdinClientException("Invalid Push notification subscription request");
            }

            
            await notificationService.AddDeviceAsync(subscription, WebOdinContext);

            HttpContext.Response.ContentType = "text/plain";
            return Ok();
        }

        [HttpPost("subscribe-firebase")]
        public async Task<IActionResult> SubscribeDevice([FromBody] PushNotificationSubscribeFirebaseRequest request)
        {
            var subscription = new PushNotificationSubscription
            {
                FriendlyName = request.FriendlyName,
                Endpoint = request.Endpoint,
                FirebaseDeviceToken = request.DeviceToken,
                FirebaseDevicePlatform = request.DevicePlatform,
            };

            _logger.LogDebug("SubscribeDevice: adding {DeviceToken}", subscription.FirebaseDeviceToken);

            if (string.IsNullOrWhiteSpace(subscription.FirebaseDeviceToken))
            {
                throw new OdinClientException("Invalid Push notification subscription request: missing device token");
            }

            if (string.IsNullOrWhiteSpace(subscription.FirebaseDevicePlatform))
            {
                throw new OdinClientException("Invalid Push notification subscription request: missing device platform");
            }

            
            await notificationService.AddDeviceAsync(subscription, WebOdinContext);

            HttpContext.Response.ContentType = "text/plain";
            return Ok();
        }


        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscriptionDetails()
        {
            
            var subscription = await notificationService.GetDeviceSubscriptionAsync(WebOdinContext);
            if (null == subscription)
            {
                return NotFound();
            }

            return new JsonResult(subscription.Redacted());
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAllSubscriptions()
        {
            
            var allSubscriptions = await notificationService.GetAllSubscriptionsAsync(WebOdinContext);
            if (null == allSubscriptions)
            {
                return NotFound();
            }

            return new JsonResult(allSubscriptions.Select(s => s.Redacted()));
        }

        [HttpPost("unsubscribe")]
        public async Task<IActionResult> RemoveDevice()
        {
            
            await notificationService.RemoveDeviceAsync(WebOdinContext);
            return Ok();
        }

        [HttpDelete("subscription")]
        public async Task<IActionResult> RemoveDevice(Guid key)
        {
            
            await notificationService.RemoveDeviceAsync(key, WebOdinContext);
            return Ok();
        }

        [HttpPost("unsubscribeAll")]
        public async Task<IActionResult> RemoveAllDevices()
        {
            
            await notificationService.RemoveAllDevicesAsync(WebOdinContext);
            return Ok();
        }

        [HttpPost("push")]
        public async Task<IActionResult> Push([FromBody] AppNotificationOptions options)
        {
            var caller = WebOdinContext.GetCallerOdinIdOrFail();
            
            await notificationService.EnqueueNotification(caller, options, WebOdinContext);
            return Ok();
        }
    }
}