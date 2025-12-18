using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileReadonlyByUidController(
        ILogger<V2DriveFileReadonlyByUidController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        public const string ByUniqueId = "by-uid/{uid:guid}";

        [HttpGet($"{ByUniqueId}/header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeaderByUid(
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid)
        {
            var result = await GetFileHeaderByUniqueIdInternal(uid, driveId);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet(ByUniqueId + "/payload/{key}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetPayloadByUniqueId(
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid,
            [FromRoute] string key,
            [FromQuery] int? start,
            [FromQuery] int? length)
        {
            FileChunk chunk = this.GetChunk(start == 0 ? null : start, length == 0 ? null : length);

            var header = await this.GetFileHeaderByUniqueIdInternal(uid, driveId);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
            var payload = await GetPayloadStream(file, key, chunk);

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
        }

        [HttpGet(ByUniqueId + "/{payloadKey}/thumb")]
        [HttpGet(ByUniqueId + "/{payloadKey}/thumb.{extension}")] // for link-preview support in signal/whatsapp
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetThumbnailByUniqueId(
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid,
            [FromRoute] string payloadKey,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] bool directMatchOnly)
        {
            logger.LogDebug("V2 call to get file thumb");

            var header = await this.GetFileHeaderByUniqueIdInternal(uid, driveId);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
            return await GetThumbnail(file, width, height, payloadKey, directMatchOnly);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByUniqueIdInternal(Guid clientUniqueId, Guid driveId)
        {
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var options = new ResultOptions()
            {
                MaxRecords = 1,
                IncludeHeaderContent = true,
                ExcludePreviewThumbnail = false
            };
            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, options, WebOdinContext);
            return result;
        }
    }
}