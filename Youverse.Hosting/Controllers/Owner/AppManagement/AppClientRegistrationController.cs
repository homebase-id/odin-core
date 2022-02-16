using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1 + "/clients")]
    [AuthorizeOwnerConsole]
    public class AppClientRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppClientRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        [HttpPost]
        public async Task<IActionResult> RegisterClient(AppClientRegistrationRequest request)
        {
            var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);
            var reg = await _appRegistration.RegisterClient(request.ApplicationId, clientPublicKey);
            return new JsonResult(reg);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAppClientRegistration(Guid id)
        {
            var reg = await _appRegistration.GetClientRegistration(id);
            return new JsonResult(reg);
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredAppDeviceList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var reg = await _appRegistration.GetClientRegistrationList(new PageOptions(pageNumber, pageSize));
            return new JsonResult(reg);
        }

        [HttpPost("revoke")]
        public async Task<NoResultResponse> RevokeAppClient([FromQuery] Guid appId, [FromQuery] string deviceId64)
        {
            throw new NotImplementedException("");
        }

        [HttpPost("allow")]
        public async Task<NoResultResponse> RemoveAppDeviceRevocation([FromQuery] Guid appId, [FromQuery] string deviceId64)
        {
            throw new NotImplementedException("");
        }
    }
}