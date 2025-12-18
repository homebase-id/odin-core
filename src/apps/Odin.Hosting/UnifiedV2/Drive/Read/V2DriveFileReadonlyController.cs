using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileReadonlyController(
        ILogger<V2DriveFileReadonlyController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId)
        {
            logger.LogDebug("V2 call to get file header");
            var storage = this.GetHttpFileSystemResolver().ResolveFileSystem().Storage;
            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            var result = await storage.GetSharedSecretEncryptedHeader(file, WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        // Full payload (optional query range)
        [HttpGet("payload/{key}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetPayload(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string key,
            [FromQuery] int? start,
            [FromQuery] int? length)
        {
            logger.LogDebug("V2 call to get file payload");

            return await GetPayloadInternal(
                driveId,
                fileId,
                key,
                start,
                length);
        }
        
        // Ranged payload (route-based)
        [HttpGet("payload/{key}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public Task<IActionResult> GetPayload(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string key,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return GetPayloadInternal(
                driveId,
                fileId,
                key,
                start,
                length);
        }

        [HttpGet("thumb")]
        [HttpGet("thumb.{extension}")] // for link-preview support in signal/whatsapp
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetThumbnail(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] string payloadKey,
            [FromQuery] bool directMatchOnly)
        {
            logger.LogDebug("V2 call to get file thumb");

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            return await GetThumbnail(file, width, height, payloadKey, directMatchOnly);
        }

        [HttpGet("transfer-history")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<FileTransferHistoryResponse> GetFileTransferHistory(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId)
        {
            WebOdinContext.Caller.AssertCallerIsOwner();

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            var storage = GetHttpFileSystemResolver().ResolveFileSystem().Storage;
            var (count, history) = await storage.GetTransferHistory(file, WebOdinContext);
            if (history == null)
            {
                Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return new FileTransferHistoryResponse()
            {
                OriginalRecipientCount = count,
                History = history
            };
        }
        
        private async Task<IActionResult> GetPayloadInternal(
            Guid driveId,
            Guid fileId,
            string key,
            int? start,
            int? length)
        {
            var file = new InternalDriveFileId
            {
                DriveId = driveId,
                FileId = fileId
            };
            
            FileChunk chunk = GetChunk(start, length);

            var payload = await GetPayloadStream(
                file,
                key,
                chunk);

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
        }

    }
}