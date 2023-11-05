using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveStorageController : DriveStorageControllerBase
    {
        private readonly ILogger<OwnerDriveStorageController> _logger;

        public OwnerDriveStorageController(
            ILogger<OwnerDriveStorageController> logger,
            FileSystemResolver fileSystemResolver,
            ITransitService transitService) :
            base(logger, fileSystemResolver, transitService)
        {
            _logger = logger;
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
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type, [FromQuery] string key)
        {
            FileChunk chunk = null;
            if (Request.Headers.TryGetValue("Range", out var rangeHeaderValue) &&
                RangeHeaderValue.TryParse(rangeHeaderValue, out var range))
            {
                var firstRange = range.Ranges.First();
                if (firstRange.From != null && firstRange.To != null)
                {
                    HttpContext.Response.StatusCode = 206;

                    int start = Convert.ToInt32(firstRange.From ?? 0);
                    int end = Convert.ToInt32(firstRange.To ?? int.MaxValue);

                    chunk = new FileChunk()
                    {
                        Start = start,
                        Length = end - start + 1
                    };
                }
            }

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
                    Key = key,
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
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] string payloadKey, [FromQuery] Guid alias, [FromQuery] Guid type, [FromQuery] int width,
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
                Height = height,
                PayloadKey = payloadKey,
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

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deletepayload")]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await base.DeletePayload(request);
        }

        /// <summary>
        /// Hard deletes a file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("harddelete")]
        public async Task<IActionResult> HardDeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = OdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);

            if (request.Recipients != null && request.Recipients.Any())
            {
                throw new OdinClientException("Cannot specify recipients when hard-deleting a file", OdinClientErrorCode.InvalidRecipient);
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
