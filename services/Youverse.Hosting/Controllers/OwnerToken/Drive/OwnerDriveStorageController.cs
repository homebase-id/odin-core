using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveStorageController : DriveReadStorageControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveStorageService _driveStorageService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitService _transitService;

        /// <summary />
        public OwnerDriveStorageController(DotYouContextAccessor contextAccessor, IDriveStorageService driveStorageService, IAppService appService, ITransitService transitService) : base(contextAccessor,
            driveStorageService, appService)
        {
            _contextAccessor = contextAccessor;
            _driveStorageService = driveStorageService;
            _appService = appService;
            _transitService = transitService;
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("header")]
        public override async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            return await base.GetFileHeader(request);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            return await base.GetFileHeader(
                new ExternalFileIdentifier()
                {
                    FileId = fileId,
                    TargetDrive = new TargetDrive()
                    {
                        Alias = alias,
                        Type = type
                    }
                });
        }

        /// <summary>
        /// Retrieves a file's payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("payload")]
        public override async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            return await base.GetPayloadStream(request);
        }


        /// <summary>
        /// Retrieves a file's payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            return await base.GetPayloadStream(new ExternalFileIdentifier()
            {
                FileId = fileId,
                TargetDrive = new()
                {
                    Alias = alias,
                    Type = type
                }
            });
        }

        /// <summary>
        /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            return await base.GetThumbnail(request);
        }

        /// <summary>
        /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type, [FromQuery] int width, [FromQuery] int height)
        {
            return await base.GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = fileId,
                    TargetDrive = new()
                    {
                        Alias = alias,
                        Type = type
                    }
                },
                Width = width,
                Height = height
            });
        }

        /// <summary>
        /// Deletes a file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive);

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = await _appService.DeleteFile(file, request.Recipients);
            if (result.LocalFileNotFound)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Hard deletes a file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("harddelete")]
        public async Task<IActionResult> HardDeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive);

            if (request.Recipients != null && request.Recipients.Any())
            {
                throw new YouverseClientException("Cannot specify recipients when hard-deleting a file", YouverseClientErrorCode.InvalidRecipient);
            }

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            await _driveStorageService.HardDeleteLongTermFile(file);
            return Ok();
        }
    }
}