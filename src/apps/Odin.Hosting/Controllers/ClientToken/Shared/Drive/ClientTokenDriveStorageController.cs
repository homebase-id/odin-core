using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Quartz.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.DriveV1)]
    [Route(GuestApiPathConstants.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveStorageController(
        ILogger<ClientTokenDriveStorageController> logger,
        FileSystemResolver fileSystemResolver,
        IPeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(fileSystemResolver, peerOutgoingTransferService)
    {
        private readonly ILogger<ClientTokenDriveStorageController> _logger = logger;

        /// <summary>
        /// Returns the file header
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/header")]
        public new async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            return await base.GetFileHeader(request);
        }

        [HttpGet("files/header")]
        public async Task<IActionResult> GetFileHeaderAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias,
            [FromQuery] Guid type)
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
        /// Returns the payload for a given file
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/payload")]
        public new async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
        {
            return await base.GetPayloadStream(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] string key,
            [FromQuery] int? chunkStart, [FromQuery] int? chunkLength)
        {
            FileChunk chunk = this.GetChunk(chunkStart, chunkLength);
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
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/thumb")]
        public new async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            return await base.GetThumbnail(request);
        }

        [HttpGet("files/thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias,
            [FromQuery] Guid type,
            [FromQuery] int width, [FromQuery] int height,
            [FromQuery] string payloadKey)
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
                Height = height,
                PayloadKey = payloadKey
            });
        }

        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/delete")]
        public new async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            return await base.DeleteFile(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/deletefileidbatch")]
        public new async Task<IActionResult> DeleteFileIdBatch([FromBody] DeleteFileIdBatchRequest request)
        {
            return await base.DeleteFileIdBatch(request);
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/deletegroupidbatch")]
        public new async Task<IActionResult> DeleteFilesByGroupIdBatch([FromBody] DeleteFilesByGroupIdBatchRequest request)
        {
            return await base.DeleteFilesByGroupIdBatch(request);
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/deletepayload")]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await base.DeletePayload(request);
        }
    }
}