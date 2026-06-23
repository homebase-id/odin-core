using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByUniqueId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryByUidController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid)
        {
            return await peerDriveQueryService.FileExistsOnRemoteByUniqueId(
                (OdinId)odinId, driveId, uid, WebOdinContext);
        }
    }

    /// <summary>
    /// Reads a single file's existence, header, payload, and thumbnails from a drive hosted by another
    /// (peer) identity, addressed by GlobalTransitId — the V2 "over peer" equivalent of the V1
    /// <c>/api/apps/v1/transit/query/{header,payload,thumb}_byglobaltransitid</c> endpoints. All
    /// shared-secret re-encryption is performed inside <see cref="PeerDriveQueryService"/>; this
    /// controller only forwards the resulting streams/headers. Mirrors
    /// <see cref="V2DrivePeerFileReadonlyController"/> (the by-fileId twin).
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByGtid)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryByGtidController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid)
        {
            return await peerDriveQueryService.FileExistsOnRemoteByGlobalTransitId(
                (OdinId)odinId, driveId, gtid, WebOdinContext);
        }

        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid)
        {
            AssertIsValidOdinId(odinId, out var id);

            var result = await peerDriveQueryService.GetFileHeaderByGlobalTransitIdAsync(
                id, ToGtidFile(driveId, gtid), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        // Full payload (Range header honored)
        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey)
        {
            return GetPayloadInternal(odinId, driveId, gtid, payloadKey, GetChunk(null, null));
        }

        // Ranged payload (route-based)
        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return GetPayloadInternal(odinId, driveId, gtid, payloadKey, GetChunk(start, length));
        }

        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetThumbnail(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromRoute] int width,
            [FromRoute] int height,
            [FromQuery] bool directMatchOnly = false)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetThumbnailByGlobalTransitIdAsync(id, ToGtidFile(driveId, gtid), payloadKey,
                    width, height, directMatchOnly, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, Guid driveId, Guid gtid, string payloadKey,
            FileChunk chunk)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, payloadStream) =
                await peerDriveQueryService.GetPayloadByGlobalTransitIdAsync(id, ToGtidFile(driveId, gtid), payloadKey,
                    chunk, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }

        // The remote resolves the drive by alias (matching the peer "exists" endpoints), so Type is left empty.
        private static GlobalTransitIdFileIdentifier ToGtidFile(Guid driveId, Guid gtid)
        {
            return new GlobalTransitIdFileIdentifier
            {
                GlobalTransitId = gtid,
                TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty }
            };
        }
    }
}
