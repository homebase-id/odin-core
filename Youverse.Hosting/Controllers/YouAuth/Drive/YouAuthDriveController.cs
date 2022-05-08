using System;
using System.Linq;
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
        public async Task<IActionResult> GetMetadata(Guid driveAlias, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias),
                FileId = fileId
            };

            //TODO: this call will encrypt the file header using the app shared secret, yet in youauth - we are using the exchange token
            //also, there may not be an exchange token in the case of a call to an anonymous file

            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid driveAlias, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);
            return new FileStreamResult(payload, "application/octet-stream");
        }


        [HttpGet("query/filetype")]
        public async Task<IActionResult> GetByFileType(Guid driveAlias, int fileType, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = await _driveService.GetDriveIdByAlias(driveAlias, true);
            var page = await _driveQueryService.GetByFileType(driveId.GetValueOrDefault(), fileType, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("query/alias")]
        public async Task<IActionResult> GetByAlias(Guid driveAlias, Guid alias, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = await _driveService.GetDriveIdByAlias(driveAlias, true);
            var page = await _driveQueryService.GetByAlias(driveId.GetValueOrDefault(), alias, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("query/tag")]
        public async Task<IActionResult> GetByTag(Guid driveAlias, Guid tag, int fileType, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = await _driveService.GetDriveIdByAlias(driveAlias, true);
            var page = await _driveQueryService.GetByTag(driveId.GetValueOrDefault(), tag, fileType, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("query/recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveAlias, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = await _driveService.GetDriveIdByAlias(driveAlias, true);
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId.GetValueOrDefault(), includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("metadata/type")]
        public async Task<IActionResult> GetDrivesByType(Guid type, int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(type, new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new YouAuthClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias
                }).ToList();

            var page = new PagedResult<YouAuthClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }
    }
}