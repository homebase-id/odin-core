using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Reads a single file's header, payload, and thumbnails from a drive hosted by another (peer)
    /// identity — the V2 "over peer" equivalent of <c>/api/apps/v1/transit/query/{header,payload,thumb}</c>.
    /// All shared-secret re-encryption is performed inside <see cref="PeerDriveQueryService"/>; this
    /// controller only forwards the resulting streams/headers.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerFileReadonlyController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId)
        {
            AssertIsValidOdinId(odinId, out var id);

            var result = await peerDriveQueryService.GetFileHeaderAsync(
                id, ToExternalFile(driveId, fileId), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

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
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey)
        {
            return GetPayloadInternal(odinId, driveId, fileId, payloadKey, GetChunk(null, null));
        }

        // Ranged payload (route-based)
        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return GetPayloadInternal(odinId, driveId, fileId, payloadKey, GetChunk(start, length));
        }

        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetThumbnail(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int width,
            [FromRoute] int height)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetThumbnailAsync(id, ToExternalFile(driveId, fileId), width, height,
                    payloadKey, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, Guid driveId, Guid fileId, string payloadKey,
            FileChunk chunk)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, payloadStream) = await peerDriveQueryService.GetPayloadStreamAsync(
                id, ToExternalFile(driveId, fileId), payloadKey, chunk, GetHttpFileSystemResolver().GetFileSystemType(),
                WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }

        // The remote resolves the drive by alias (matching the peer "exists" endpoints), so Type is left empty.
        private static ExternalFileIdentifier ToExternalFile(Guid driveId, Guid fileId)
        {
            return new ExternalFileIdentifier
            {
                FileId = fileId,
                TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty }
            };
        }
    }
}
