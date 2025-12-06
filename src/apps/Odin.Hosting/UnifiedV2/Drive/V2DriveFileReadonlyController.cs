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

namespace Odin.Hosting.UnifiedV2.Drive
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
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            logger.LogDebug("V2 call to get file header");
            var storage = this.GetHttpFileSystemResolver().ResolveFileSystem(fileSystemType).Storage;
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

        [HttpGet("payload")]
        public async Task<IActionResult> GetPayload(
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromQuery] string key,
            [FromQuery] int? start,
            [FromQuery] int? length,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            logger.LogDebug("V2 call to get file payload");

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            FileChunk chunk = this.GetChunk(start == 0 ? null : start, length == 0 ? null : length);
            var payload = await GetPayloadStream(file, key, chunk, fileSystemType);

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
        }

        [HttpGet("thumb")]
        [HttpGet("thumb.{extension}")] // for link-preview support in signal/whatsapp
        public async Task<IActionResult> GetThumbnail(
            [FromRoute]Guid driveId,
            [FromRoute]Guid fileId,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] string payloadKey,
            [FromQuery] bool directMatchOnly,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            logger.LogDebug("V2 call to get file thumb");

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            return await GetThumbnail(file, width, height, payloadKey, directMatchOnly, fileSystemType);
        }

        [HttpGet("transfer-history")]
        public async Task<FileTransferHistoryResponse> GetFileTransferHistory(
            [FromRoute]Guid driveId,
            [FromRoute]Guid fileId,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            WebOdinContext.Caller.AssertCallerIsOwner();

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            var storage = GetHttpFileSystemResolver().ResolveFileSystem(fileSystemType).Storage;
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
    }
}