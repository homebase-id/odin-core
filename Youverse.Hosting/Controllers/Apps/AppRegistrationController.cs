using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Apps
{
    [ApiController]
    [Route("/api/admin/apps")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class AppRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredApps([FromQuery]int pageNumber, [FromQuery]int pageSize)
        {
            var apps = await _appRegistration.GetRegisteredApps(new PageOptions(pageNumber, pageSize));
            return new JsonResult(apps);
        }

        [HttpGet("{appId}")]
        public async Task<IActionResult> GetRegisteredApp(Guid appId)
        {
            var reg = await _appRegistration.GetAppRegistration(appId);
            return new JsonResult(reg);
        }

        [HttpPost]
        public async Task<IActionResult> RegisterApp([FromBody]AppRegistrationPayload appRegistration)
        {
            var reg = await _appRegistration.RegisterApp(appRegistration.ApplicationId, appRegistration.Name);
            return new JsonResult(reg);
        }

        [HttpPost("revoke/{appId}")]
        public async Task<NoResultResponse> RevokeApp(Guid appId)
        {
            await _appRegistration.RevokeApp(appId);
            return new NoResultResponse(true);
        }
        
        [HttpPost("allow/{appId}")]
        public async Task<NoResultResponse> RemoveRevocation(Guid appId)
        {
            await _appRegistration.RemoveAppRevocation(appId);
            return new NoResultResponse(true);
        }
    }
}