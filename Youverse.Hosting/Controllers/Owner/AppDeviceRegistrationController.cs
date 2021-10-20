using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.AppRegistration;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Owner
{
    [ApiController]
    [Route("/api/admin/apps/devices")]
    //[Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
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
        public async Task<IActionResult> GetRegisteredAppDevice(Guid applicationId, string deviceId64)
        {
            var uniqueDeviceId = Convert.FromBase64String(deviceId64);
            var reg = await _appRegistration.GetAppDeviceRegistration(applicationId, uniqueDeviceId);
            return new JsonResult(reg);
        }

        [HttpGet("apps/devices")]
        public async Task<IActionResult> GetRegisteredAppDeviceList(int pageNumber, int pageSize)
        {
            var reg = await _appRegistration.GetRegisteredAppDevices(new PageOptions(pageNumber, pageSize));
            return new JsonResult(reg);
        }
        
        [HttpDelete("/{applicationId}/{deviceId64}")]
        public async Task<IActionResult> RevokeAppDevice(Guid applicationId, string deviceId64)
        {
            var bytes = Convert.FromBase64String(deviceId64);
            await _appRegistration.RevokeAppDevice(applicationId, bytes);
            return new JsonResult(true);
        }
    }
}