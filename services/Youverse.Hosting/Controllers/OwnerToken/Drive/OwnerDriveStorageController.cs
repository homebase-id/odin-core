using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveStorageController : DriveStorageControllerBase
    {
        public OwnerDriveStorageController(FileSystemResolver fileSystemResolver, ITransitService transitService) :
            base(fileSystemResolver, transitService)
        {
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("header")]
        public new async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
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
        public new async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
        {
            return await base.GetPayloadStream(request);
        }


        /// <summary>
        /// Retrieves a file's payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] int? chunkStart, [FromQuery] int? chunkLength)
        {
            var chunk = chunkStart.HasValue
                ? new FileChunk()
                {
                    Start = chunkStart.GetValueOrDefault(),
                    Length = chunkLength.GetValueOrDefault(int.MaxValue)
                }
                : null;

            return await base.GetPayloadStream(
                new GetPayloadRequest()
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
                    Chunk = chunk
                });
        }

        /// <summary>
        /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("thumb")]
        public new async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            return await base.GetThumbnail(request);
        }

        /// <summary>
        /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type, [FromQuery] int width,
            [FromQuery] int height)
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
        public new async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            return await base.DeleteFile(request);
        }

        /// <summary>
        /// Hard deletes a file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("harddelete")]
        public async Task<IActionResult> HardDeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.File.TargetDrive);

            if (request.Recipients != null && request.Recipients.Any())
            {
                throw new YouverseClientException("Cannot specify recipients when hard-deleting a file", YouverseClientErrorCode.InvalidRecipient);
            }

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            await base.GetFileSystemResolver().ResolveFileSystem().Storage.HardDeleteLongTermFile(file);
            return Ok();
        }
    }
}