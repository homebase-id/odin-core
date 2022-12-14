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

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitService _transitService;

        public OwnerDriveStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService, ITransitService transitService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
            _transitService = transitService;
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var result = await _appService.GetClientEncryptedFileHeader(file);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            return await GetFileHeader(
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
        /// Retrieves a file's encrypted payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var payload = await _driveService.GetPayloadStream(file);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await _appService.GetClientEncryptedFileHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(TransitConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(TransitConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(TransitConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            return await GetPayloadStream(new ExternalFileIdentifier()
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
        /// Retrieves an encrypted thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            var payload = await _driveService.GetThumbnailPayloadStream(file, request.Width, request.Height);

            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await _appService.GetClientEncryptedFileHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(TransitConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(TransitConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(TransitConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type, [FromQuery] int width, [FromQuery] int height)
        {
            return await GetThumbnail(new GetThumbnailRequest()
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

            if (request.Recipients.Any())
            {
                throw new YouverseClientException("Cannot specify recipients when hard-deleting a file", YouverseClientErrorCode.InvalidRecipient);
            }

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            await _driveService.HardDeleteLongTermFile(file);
            return Ok();
        }
    }
}
