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
    [Route("/api/admin/apps")]
    //[Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class AppRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistration;

        public AppRegistrationController(IAppRegistrationService appRegistration)
        {
            _appRegistration = appRegistration;
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredApps(int pageNumber, int pageSize)
        {
            var apps = await _appRegistration.GetRegisteredApps(new PageOptions(pageNumber, pageSize));
            return new JsonResult(apps);
        }

        [HttpGet("/{applicationId}")]
        public async Task<IActionResult> GetRegisteredApp(Guid applicationId)
        {
            var reg = await _appRegistration.GetAppRegistration(applicationId);
            return new JsonResult(reg);
        }

        [HttpPost]
        public async Task<IActionResult> RegisterApp(AppRegistrationPayload appRegistration)
        {
            var reg = await _appRegistration.RegisterApp(appRegistration.ApplicationId, appRegistration.Name);
            return new JsonResult(reg);
        }

        [HttpPost("/revoke/{applicationId}")]
        public async Task<IActionResult> RevokeApp(Guid applicationId)
        {
            await _appRegistration.RevokeApp(applicationId);
            return new JsonResult(true);
        }
        
        [HttpPost("/allow/{applicationId}")]
        public async Task<IActionResult> RemoveRevocation(Guid applicationId)
        {
            await _appRegistration.RemoveAppRevocation(applicationId);
            return new JsonResult(true);
        }
    }
}