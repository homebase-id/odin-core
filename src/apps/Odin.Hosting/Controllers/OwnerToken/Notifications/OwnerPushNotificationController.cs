#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications
{
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerPushNotificationController : Controller
    {
        private readonly PushNotificationService _notificationService;
        private readonly OdinContextAccessor _contextAccessor;

        public OwnerPushNotificationController(PushNotificationService notificationService, OdinContextAccessor contextAccessor)
        {
            _notificationService = notificationService;
            _contextAccessor = contextAccessor;
        }

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
            
            await _notificationService.AddDevice(subscription);

            HttpContext.Response.ContentType = "text/plain";
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
        
        [HttpDelete("subscription")]
        public async Task<IActionResult> RemoveDevice(Guid key)
        {
            await _notificationService.RemoveDevice(key);
            return Ok();
        }

        [HttpPost("unsubscribeAll")]
        public async Task<IActionResult> RemoveAllDevices()
        {
            await _notificationService.RemoveAllDevices();
            return Ok();
        }

        [HttpPost("push")]
        public async Task<IActionResult> Push([FromBody] AppNotificationOptions options)
        {
            var caller = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
            await _notificationService.EnqueueNotification(caller, options);
            return Ok();
        }
    }
}