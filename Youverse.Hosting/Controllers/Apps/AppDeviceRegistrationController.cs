using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Apps
{
    [ApiController]
    [Route("/api/admin/apps/devices")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class AppDeviceRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppDeviceRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        [HttpPost]
        public async Task<IActionResult> RegisterAppOnDevice(AppDeviceRegistrationPayload appDeviceRegistrationPayload)
        {
            //TODO: determine the shared key
            //Note: this assumes the shared secret comes from the client.

            var uniqueDeviceId = Convert.FromBase64String(appDeviceRegistrationPayload.DeviceId64);
            var sharedSecret = Convert.FromBase64String(appDeviceRegistrationPayload.SharedSecret64);
            var reg = await _appRegistration.RegisterAppOnDevice(appDeviceRegistrationPayload.ApplicationId, uniqueDeviceId, sharedSecret);
            return new JsonResult(reg);
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredAppDevice([FromQuery] Guid appId, [FromQuery] string deviceId64)
        {
            //TODO: should all of these fields be returned to the client?
            var uniqueDeviceId = Convert.FromBase64String(deviceId64);
            var reg = await _appRegistration.GetAppDeviceRegistration(appId, uniqueDeviceId);
            return new JsonResult(reg);
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredAppDeviceList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var reg = await _appRegistration.GetRegisteredAppDevices(new PageOptions(pageNumber, pageSize));
            return new JsonResult(reg);
        }

        [HttpPost("/revoke")]
        public async Task<NoResultResponse> RevokeAppDevice([FromQuery] Guid appId, [FromQuery] string deviceId64)
        {
            var bytes = Convert.FromBase64String(deviceId64);
            await _appRegistration.RevokeAppDevice(appId, bytes);
            return new NoResultResponse(true);
        }

        [HttpPost("/allow")]
        public async Task<NoResultResponse> RemoveAppDeviceRevocation([FromQuery] Guid appId, [FromQuery] string deviceId64)
        {
            var bytes = Convert.FromBase64String(deviceId64);
            await _appRegistration.RemoveAppDeviceRevocation(appId, bytes);
            return new NoResultResponse(true);
        }
    }
}