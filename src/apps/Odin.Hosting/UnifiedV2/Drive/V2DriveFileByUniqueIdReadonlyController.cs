using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByUniqueId)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileByUniqueIdReadonlyController(
        ILogger<V2DriveFileByUniqueIdReadonlyController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderByUid(
            [FromRoute]Guid driveId,
            [FromRoute]Guid uid,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var result = await GetFileHeaderByUniqueIdInternal(uid, driveId, fileSystemType);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadByUniqueId(
            [FromRoute]Guid driveId,
            [FromRoute]Guid uid,
            [FromQuery] string key,
            [FromQuery] int? start,
            [FromQuery] int? length,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            
            FileChunk chunk = this.GetChunk(start == 0 ? null : start, length == 0 ? null : length);
            
            var header = await this.GetFileHeaderByUniqueIdInternal(uid, driveId, fileSystemType);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
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
            [FromRoute]Guid uid,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] string payloadKey,
            [FromQuery] bool directMatchOnly,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            logger.LogDebug("V2 call to get file thumb");

            var header = await this.GetFileHeaderByUniqueIdInternal(uid, driveId, fileSystemType);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
            return await GetThumbnail(file, width, height, payloadKey, directMatchOnly, fileSystemType);
        }
        
        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByUniqueIdInternal(Guid clientUniqueId, Guid driveId, 
            FileSystemType fileSystemType)
        {
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem(fileSystemType).Query;
            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, excludePreviewThumbnail: false, odinContext: WebOdinContext);
            return result;
        }
    }
}