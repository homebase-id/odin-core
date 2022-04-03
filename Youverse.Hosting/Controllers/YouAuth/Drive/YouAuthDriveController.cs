using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers.YouAuth.Drive
{
    [ApiController]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class YouAuthDriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly IDriveQueryService _driveQueryService;

        private readonly DotYouContextAccessor _contextAccessor;

        public YouAuthDriveStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService, IDriveQueryService driveQueryService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
            _driveQueryService = driveQueryService;
        }

        //
        
        [HttpGet("files/header")]
        public async Task<IActionResult> GetMetadata(Guid driveIdentifier, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier),
                FileId = fileId
            };

            //TODO: this call will encrypt the file header using the app shared secret, yet in youauth - we are using the exchange token
            //also, there may not be an exchange token in the case of a call to an anonymous file

            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid driveIdentifier, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }

        // [HttpGet("filetype")]
        // public async Task<IActionResult> GetByFileType(int fileType, bool includeContent, int pageNumber, int pageSize)
        // {
        //     var driveId = _context.GetCurrent().AppContext.DriveId.GetValueOrDefault();
        //     var page = await _driveQueryService.GetByFiletype(driveId, fileType, includeContent, new PageOptions(pageNumber, pageSize));
        //     return new JsonResult(page);
        // }

        [HttpGet("query/alias")]
        public async Task<IActionResult> GetByAlias(Guid driveIdentifier, Guid alias, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier);

            var page = await _driveQueryService.GetByAlias(driveId, alias, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
        
        [HttpGet("query/tag")]
        public async Task<IActionResult> GetByTag(Guid driveIdentifier, Guid tag, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier);

            var page = await _driveQueryService.GetByTag(driveId, tag, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("query/recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveIdentifier, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier);
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
    }
}