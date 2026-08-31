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
    [Route(UnifiedApiRouteConstants.ByUniqueId)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileReadonlyByUidController(
        ILogger<V2DriveFileReadonlyByUidController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        public const string ByUniqueId = "by-uid/{uid:guid}";

        [HttpGet("header")]
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

        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetPayloadByUniqueId(
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid,
            [FromRoute] string payloadKey)
        {
            return await this.GetPayloadByUniqueIdInternal(driveId, uid, payloadKey);
        }

        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetPayloadByUniqueId(
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            FileChunk chunk = this.GetChunk(start == 0 ? null : start, length == 0 ? null : length);

            return await GetPayloadByUniqueIdInternal(driveId, uid, payloadKey, chunk);
        }

        [HttpGet("payload/{payloadKey}/thumb")]
        [HttpGet("payload/{payloadKey}/thumb.{extension}")] // for link-preview support in signal/whatsapp
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
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
            return await GetThumbnailInternal(file, width, height, payloadKey, directMatchOnly);
        }

        /// <summary>
        /// Brings the file's death forward to now. Anonymous on purpose - the caller demonstrably
        /// holds the link, so all they can surrender is remaining lifetime; the service refuses files
        /// with no Ttl, so permanent data cannot be destroyed this way. (A POST on a controller named
        /// Readonly is a wart; it lives here because it shares the by-uid resolution.)
        /// </summary>
        //
        // ██████████████████████████████████████████████████████████████████████████████████████
        // ██ SECURITY DEBT - MUST BE FIXED BEFORE THIS SHIPS TO REAL USERS                    ██
        // ██                                                                                  ██
        // ██ This endpoint must ALSO require drive.BlockAnonymousEnumeration == true (the     ██
        // ██ flag does not exist yet). Until then, on an ENUMERABLE anonymous drive a scraper ██
        // ██ can walk the file list and expire-now every TTL'd file on it - mass destruction  ██
        // ██ of public-but-expiring content. The capability argument ("the caller already     ██
        // ██ holds the link") only holds on non-enumerable drives, where you can only address ██
        // ██ what you were given. When BlockAnonymousEnumeration lands, add the gate here     ██
        // ██ (or in HastenExpiryAsync) and delete this banner.                                ██
        // ██████████████████████████████████████████████████████████████████████████████████████
        //
        [HttpPost("expire-now")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> ExpireNow([FromRoute] Guid driveId, [FromRoute] Guid uid)
        {
            var header = await GetFileHeaderByUniqueIdInternal(uid, driveId);
            if (header == null)
            {
                return NotFound();
            }

            var fs = GetHttpFileSystemResolver().ResolveFileSystem();
            var file = new InternalDriveFileId(driveId, header.FileId);
            var hastened = await fs.Storage.HastenExpiryAsync(file, WebOdinContext);
            return hastened ? Ok() : NotFound();
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

        private async Task<IActionResult> GetPayloadByUniqueIdInternal(Guid driveId, Guid uid, string payloadKey, FileChunk chunk = null)
        {
            var header = await this.GetFileHeaderByUniqueIdInternal(uid, driveId);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
            var payload = await GetPayloadStream(file, payloadKey, chunk);

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
        }
    }
}