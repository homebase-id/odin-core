using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.App;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route("/api/apps/v1/drive")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveStorageController : ControllerBase
    {
        private readonly IDriveService _driveService;
        private readonly IDriveQueryService _queryService;
        private readonly DotYouContext _context;

        public DriveStorageController(DotYouContext context, IDriveService driveService, IDriveQueryService queryService)
        {
            _context = context;
            _driveService = driveService;
            _queryService = queryService;
        }

        [HttpGet("files")]
        public async Task<IActionResult> GetFile(Guid fileId)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();

            var file = new DriveFileId() { DriveId = driveId, FileId = fileId };

            var ekh = await _driveService.GetEncryptedKeyHeader(file);
            var metadata = await _driveService.GetMetadata(file);
            
        }
        
        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid fileId)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();

            var file = new DriveFileId() { DriveId = driveId, FileId = fileId };
            
            var payload = await _driveService.GetPayloadStream(file);

            FileStreamResult result = new FileStreamResult(payload, "application/octet-stream");
            return result;
        }
    }
}