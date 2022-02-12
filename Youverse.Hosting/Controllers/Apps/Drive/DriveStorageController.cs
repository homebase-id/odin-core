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


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
    public class DriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContext _context;

        public DriveStorageController(DotYouContext context, IDriveService driveService, IAppService appService)
        {
            _context = context;
            _driveService = driveService;
            _appService = appService;
        }

        [HttpGet("files/header")]
        public async Task<IActionResult> GetMetadata(Guid fileId)
        {
            var file = new DriveFileId()
            {
                DriveId = _context.AppContext.DriveId.GetValueOrDefault(),
                FileId = fileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid fileId)
        {
            var file = new DriveFileId()
            {
                DriveId = _context.AppContext.DriveId.GetValueOrDefault(),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }
    }
}