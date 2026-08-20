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
    /// Reads payloads and thumbnails on a local drive addressed by globalTransitId.
    /// </summary>
    /// <remarks>
    /// A follower's feed row carries only the author's globalTransitId - not the author's local
    /// fileId, and not a uniqueId - so by-fileId and by-uid cannot address a followed post's media
    /// on the author's host. Until now the only by-gtid read was the OwnerOrApp-gated peer route,
    /// which a CDN edge cannot use.
    ///
    /// Being under <see cref="UnifiedApiRouteConstants.DrivesRoot"/> with a /payload/ or /thumb
    /// segment makes these routes CDN-eligible automatically; CdnAuthPathHandler matches on path
    /// shape and needs no change.
    /// </remarks>
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByGtid)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileReadonlyByGtidController(
        ILogger<V2DriveFileReadonlyByGtidController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetPayloadByGtid(
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey)
        {
            return await this.GetPayloadByGtidInternal(driveId, gtid, payloadKey);
        }

        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetPayloadByGtid(
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            FileChunk chunk = this.GetChunk(start == 0 ? null : start, length == 0 ? null : length);

            return await GetPayloadByGtidInternal(driveId, gtid, payloadKey, chunk);
        }

        [HttpGet("payload/{payloadKey}/thumb")]
        [HttpGet("payload/{payloadKey}/thumb.{extension}")] // for link-preview support in signal/whatsapp
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetThumbnailByGtid(
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] bool directMatchOnly)
        {
            logger.LogDebug("V2 call to get file thumb by gtid");

            var header = await this.GetFileHeaderByGtidInternal(driveId, gtid);
            if (null == header)
            {
                return NotFound();
            }

            var file = new InternalDriveFileId(driveId, header.FileId);
            return await GetThumbnailInternal(file, width, height, payloadKey, directMatchOnly);
        }

        // Deliberately not DriveStorageControllerBase.GetFileHeaderByGlobalTransitId: that one gates on
        // PermissionsContext.AssertCanReadDrive, which has no AllowAnonymousReads bypass and so rejects an
        // anonymous or CDN caller even on a public drive. The query service's own gate does have it.
        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByGtidInternal(Guid driveId, Guid gtid)
        {
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            return await queryService.GetFileByGlobalTransitId(
                driveId,
                gtid,
                WebOdinContext,
                excludePreviewThumbnail: false,
                includeTransferHistory: false);
        }

        private async Task<IActionResult> GetPayloadByGtidInternal(Guid driveId, Guid gtid, string payloadKey,
            FileChunk chunk = null)
        {
            var header = await this.GetFileHeaderByGtidInternal(driveId, gtid);
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
