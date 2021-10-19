using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.AppRegistration;

namespace Youverse.Hosting.Controllers.Owner
{
    [ApiController]
    [Route("/api/admin/appregistration")]
    public class AppRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        [HttpGet("apps")]
        public async Task<IActionResult> GetRegisteredApps(int pageNumber, int pageSize)
        {
            var apps = await _appRegistration.GetRegisteredApps(new PageOptions(pageNumber, pageSize));
            return new JsonResult(apps);
        }

        [HttpGet("apps/{applicationId}")]
        public async Task<IActionResult> GetAppRegistration(Guid applicationId)
        {
            var reg = await _appRegistration.GetAppRegistration(applicationId);
            return new JsonResult(reg);
        }

        [HttpPost("apps")]
        public async Task<IActionResult> RegisterApplication(Guid applicationId, string name)
        {
            var reg = await _appRegistration.RegisterApp(applicationId, name);
            return new JsonResult(reg);
        }

        [HttpDelete("apps/{applicationId}")]
        public async Task<IActionResult> RevokeApp(Guid applicationId)
        {
            await _appRegistration.RevokeApp(applicationId);
            return new JsonResult(true);
        }

        [HttpPost("apps/devices")]
        public async Task<IActionResult> RegisterAppOnDevice(Guid applicationId, string uniqueDeviceId64, byte[] sharedSecret)
        {
            //TODO: determine the shared key
            //Note: this assumes the shared secret comes from the client.

            var uniqueDeviceId = Convert.FromBase64String(uniqueDeviceId64);
            var reg = await _appRegistration.RegisterAppOnDevice(applicationId, uniqueDeviceId, sharedSecret);
            return new JsonResult(reg);
        }

        [HttpGet("apps/devices")]
        public async Task<IActionResult> GetRegisteredAppDevice(Guid applicationId, string uniqueDeviceId64, byte[] sharedSecret)
        {
            var uniqueDeviceId = Convert.FromBase64String(uniqueDeviceId64);
            var reg = await _appRegistration.GetRegisteredAppDevice(applicationId, uniqueDeviceId);
            return new JsonResult(reg);
        }

        [HttpGet("apps/devices")]
        public async Task<IActionResult> GetRegisteredAppDeviceList(int pageNumber, int pageSize)
        {
            var reg = await _appRegistration.GetRegisteredAppDevices(new PageOptions(pageNumber, pageSize));
            return new JsonResult(reg);
        }

        [HttpDelete("apps/devices/{uniqueIdentifier64}")]
        public async Task<IActionResult> RevokeDevice(string uniqueIdentifier64)
        {
            var bytes = Convert.FromBase64String(uniqueIdentifier64);
            await _appRegistration.RevokeDevice(bytes);
            return new JsonResult(true);
        }

        [HttpDelete("apps/devices/")]
        public async Task<IActionResult> RevokeAppDevice(Guid applicationId, string devId64)
        {
            var bytes = Convert.FromBase64String(devId64);
            await _appRegistration.RevokeAppDevice(applicationId, bytes);
            return new JsonResult(true);
        }
    }
}