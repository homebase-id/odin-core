using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    [ApiController]
    [Route(OwnerApiPathConstants.AppManagementV1)]
    [AuthorizeValidOwnerToken]
    public class AppRegistrationController : Controller
    {
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly IDriveService _driveService;

        public AppRegistrationController(IAppRegistrationService appRegistrationService, IDriveService driveService)
        {
            _appRegistrationService = appRegistrationService;
            _driveService = driveService;
        }

        [HttpGet]
        public async Task<IActionResult> GetRegisteredApps([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var apps = await _appRegistrationService.GetRegisteredApps(new PageOptions(pageNumber, pageSize));
            return new JsonResult(apps);
        }

        [HttpGet("{appId}")]
        public async Task<IActionResult> GetRegisteredApp(Guid appId)
        {
            var reg = await _appRegistrationService.GetAppRegistration(appId);
            return new JsonResult(reg);
        }

        [HttpPost]
        public async Task<IActionResult> RegisterApp([FromBody] AppRegistrationRequest appRegistration)
        {
            var reg = await _appRegistrationService.RegisterApp(
                applicationId: appRegistration.ApplicationId,
                name: appRegistration.Name,
                permissions: appRegistration.PermissionSet,
                drives: appRegistration.Drives);
            return new JsonResult(reg);
        }

        [HttpPost("revoke/{appId}")]
        public async Task<NoResultResponse> RevokeApp(Guid appId)
        {
            await _appRegistrationService.RevokeApp(appId);
            return new NoResultResponse(true);
        }

        [HttpPost("allow/{appId}")]
        public async Task<NoResultResponse> RemoveRevocation(Guid appId)
        {
            await _appRegistrationService.RemoveAppRevocation(appId);
            return new NoResultResponse(true);
        }
    }
}