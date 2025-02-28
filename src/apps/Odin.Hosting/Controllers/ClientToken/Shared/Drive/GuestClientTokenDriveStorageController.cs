using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(GuestApiPathConstants.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class GuestClientTokenDriveStorageController(
        ILogger<GuestClientTokenDriveStorageController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        private readonly ILogger<GuestClientTokenDriveStorageController> _logger = logger;

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
            
            var payload = await base.GetPayloadStream(
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

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
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
        [HttpGet("files/thumb.jpg")] // for link-preview support in signal/whatsapp
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