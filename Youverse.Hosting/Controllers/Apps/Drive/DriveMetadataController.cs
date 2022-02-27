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
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        public DriveMetadataController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
        }

        [HttpGet("metadata")]
        public IActionResult GetMetadata()
        {
            var owned = _contextAccessor.GetCurrent().AppContext.OwnedDrives.Select(x => new
            {
                DriveIdentifier = x.DriveIdentifier,
                Permissions = x.Permissions
            });

            var additional = _contextAccessor.GetCurrent().AppContext.OwnedDrives.Select(x => new
            {
                DriveIdentifier = x.DriveIdentifier,
                Permissions = x.Permissions
            });

            return new JsonResult(new
            {
                Owned = owned,
                Additional = additional
            });
        }


    }
}