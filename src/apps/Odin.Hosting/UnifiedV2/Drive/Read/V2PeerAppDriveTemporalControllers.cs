using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Temporal (time-boxed) reads of a peer drive named by slug — the twin of
    /// <see cref="V2DrivePeerTemporalController"/>.  Kept in step with its guid counterpart on purpose:
    /// the temporal chain mirrors the ordinary read chain, so a drive addressable one way should be
    /// addressable the other.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppTemporalRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveTemporalController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpPost("verify")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<TemporalAccessStatus> VerifyTemporalAccess(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            return await peerDriveQueryService.VerifyTemporalAccessAsync(id, targetDrive, fst, WebOdinContext);
        }

        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> TemporalQueryBatch(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromBody] QueryBatchRequestV2 request)
        {
            OdinValidationUtils.AssertNotNull(request, "request");
            OdinValidationUtils.AssertNotNull(request.QueryParams, "QueryParams");
            OdinValidationUtils.AssertNotNull(request.ResultOptionsRequest, "ResultOptionsRequest");

            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            var fst = GetHttpFileSystemResolver().GetFileSystemType();

            var v1Request = new QueryBatchRequest
            {
                QueryParams = ToV1QueryParams(request.QueryParams, targetDrive),
                ResultOptionsRequest = request.ResultOptionsRequest,
                FileSystemType = fst
            };

            var batch = await peerDriveQueryService.GetTemporalBatchAsync(id, v1Request, fst, WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }
    }

    /// <summary>
    /// Temporal read of one file on a peer drive named by slug — the twin of
    /// <c>V2DrivePeerTemporalFileController</c>.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppTemporalByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveTemporalFileController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> TemporalGetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var result = await peerDriveQueryService.GetTemporalFileHeaderAsync(
                id, ToExternalFile(targetDrive, fileId), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return result == null ? NotFound() : new JsonResult(result);
        }

        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> TemporalGetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey)
        {
            return TemporalGetPayloadInternal(odinId, appSlug, driveSlug, fileId, payloadKey, GetChunk(null, null));
        }

        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> TemporalGetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return TemporalGetPayloadInternal(odinId, appSlug, driveSlug, fileId, payloadKey, GetChunk(start, length));
        }

        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> TemporalGetThumbnail(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int width,
            [FromRoute] int height)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetTemporalThumbnailAsync(id, ToExternalFile(targetDrive, fileId), width,
                    height, payloadKey, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> TemporalGetPayloadInternal(string odinId, string appSlug, string driveSlug,
            Guid fileId, string payloadKey, FileChunk chunk)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, payloadStream) =
                await peerDriveQueryService.GetTemporalPayloadStreamAsync(id, ToExternalFile(targetDrive, fileId),
                    payloadKey, chunk, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }
    }
}
