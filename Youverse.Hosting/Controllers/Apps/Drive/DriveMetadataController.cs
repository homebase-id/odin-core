using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.AspNetCore.WebUtilities;
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

        public DriveMetadataController(DotYouContextAccessor contextAccessor, IAppRegistrationService appRegistrationService)
        {
            _contextAccessor = contextAccessor;
            _appRegistrationService = appRegistrationService;
        }

        [HttpGet("metadata")]
        public IActionResult GetMetadata()
        {
            var appContext = _contextAccessor.GetCurrent().AppContext;

            return new JsonResult(new
            {
                Owned = appContext.OwnedDrives.Select(x => new
                {
                    DriveIdentifier = x.DriveIdentifier,
                    Permissions = x.Permissions
                }),
                Additional = appContext.OwnedDrives.Select(x => new
                {
                    DriveIdentifier = x.DriveIdentifier,
                    Permissions = x.Permissions
                })
            });
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateDrive(Guid driveIdentifier, string name, bool allowAnonymousReads)
        {
            await _appRegistrationService.CreateOwnedDrive(
                this._contextAccessor.GetCurrent().AppContext.AppId,
                driveIdentifier,
                name,
                allowAnonymousReads);

            return Ok();
        }
    }
}