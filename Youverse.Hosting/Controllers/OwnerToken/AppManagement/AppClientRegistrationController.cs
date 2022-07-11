using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1 + "/clients")]
    [AuthorizeValidOwnerToken]
    public class AppClientRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppClientRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        /// <summary>
        /// Registers a new client for using a specific app (a browser, app running on a phone, etc)
        /// </summary>
        /// <remarks>
        /// </remarks>
        [HttpPost]
        public async Task<IActionResult> RegisterClient(AppClientRegistrationRequest request)
        {
            var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey64);
            var reg = await _appRegistration.RegisterClient(request.ApplicationId, clientPublicKey);
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