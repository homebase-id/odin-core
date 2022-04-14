using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Controllers.Owner;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
    public class DriveMetadataController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly IDriveService _driveService;

        public DriveMetadataController(DotYouContextAccessor contextAccessor, IAppRegistrationService appRegistrationService, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _appRegistrationService = appRegistrationService;
            _driveService = driveService;
        }

        [HttpGet("metadata")]
        public IActionResult GetMetadata()
        {
            var appContext = _contextAccessor.GetCurrent().AppContext;

            return new JsonResult(new
            {
                Owned = appContext.OwnedDrives.Select(x => new
                {
                    DriveAlias = x.DriveAlias,
                    Permissions = x.Permissions
                }),
                Additional = appContext.OwnedDrives.Select(x => new
                {
                    DriveAlias = x.DriveAlias,
                    Permissions = x.Permissions
                })
            });
        }

        [HttpGet("metadata/type")]
        public async Task<IActionResult> GetDrivesByType(Guid type, int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(type, new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new ClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateDrive(Guid driveAlias, string name, Guid type, string metadata, bool allowAnonymousReads)
        {
            await _appRegistrationService.CreateOwnedDrive(
                this._contextAccessor.GetCurrent().AppContext.AppId,
                driveAlias,
                name,
                type,
                metadata,
                allowAnonymousReads);

            return Ok();
        }
    }
}