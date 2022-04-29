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
        private readonly IAppRegistrationService _appRegistrationService;

        public AppRegistrationController(IAppRegistrationService appRegistrationService)
        {
            _appRegistrationService = appRegistrationService;
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
                driveAlias: appRegistration.DefaultDrivePublicId,
                driveType: appRegistration.DriveType,
                driveName: appRegistration.DriveName,
                driveMetadata: appRegistration.DriveMetadata,
                createDrive: appRegistration.CreateDrive,
                canManageConnections: appRegistration.CanManageConnections);
            return new JsonResult(reg);
        }

        [HttpPost("drives/owned")]
        public async Task<IActionResult> CreateOwnedDrive(Guid appId, Guid driveAlias, string driveName, Guid type, string metadata)
        {
            await _appRegistrationService.CreateOwnedDrive(appId, driveAlias, driveName, type, metadata);
            return Ok();
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