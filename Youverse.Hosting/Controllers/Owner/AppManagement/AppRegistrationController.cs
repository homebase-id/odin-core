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
    [Route(OwnerApiPathConstants.AppManagementV1)]
    [AuthorizeOwnerConsole]
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
        public async Task<IActionResult> RegisterApp([FromBody]AppRegistrationRequest appRegistration)
        {
            var reg = await _appRegistration.RegisterApp(appRegistration.ApplicationId, appRegistration.Name, appRegistration.CreateDrive, appRegistration.DefaultDrivePublicId, appRegistration.CanManageConnections);
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