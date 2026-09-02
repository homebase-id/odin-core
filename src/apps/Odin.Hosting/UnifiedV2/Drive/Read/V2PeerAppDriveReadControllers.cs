using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Query a peer drive named by slug — the twin of <see cref="V2DrivePeerQueryBatchController"/>,
    /// which names the same drive by guid.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppDriveBySlug)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveQueryBatchController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> QueryBatch(
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

            var batch = await peerDriveQueryService.GetBatchAsync(id, v1Request, fst, WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }
    }

    /// <summary>
    /// Read one file on a peer drive named by slug, addressed by FileId — the twin of
    /// <see cref="V2DrivePeerFileReadonlyController"/>.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveFileReadonlyController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var result = await peerDriveQueryService.GetFileHeaderAsync(
                id, ToExternalFile(targetDrive, fileId), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return result == null ? NotFound() : new JsonResult(result);
        }

        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey)
        {
            return GetPayloadInternal(odinId, appSlug, driveSlug, fileId, payloadKey, GetChunk(null, null));
        }

        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return GetPayloadInternal(odinId, appSlug, driveSlug, fileId, payloadKey, GetChunk(start, length));
        }

        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetThumbnail(
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
                await peerDriveQueryService.GetThumbnailAsync(id, ToExternalFile(targetDrive, fileId), width, height,
                    payloadKey, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, string appSlug, string driveSlug,
            Guid fileId, string payloadKey, FileChunk chunk)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, payloadStream) = await peerDriveQueryService.GetPayloadStreamAsync(
                id, ToExternalFile(targetDrive, fileId), payloadKey, chunk,
                GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }
    }

    /// <summary>
    /// Existence check by UniqueId on a peer drive named by slug — the twin of
    /// <c>V2DrivePeerQueryByUidController</c>.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppByUniqueId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveQueryByUidController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid uid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            return await peerDriveQueryService.FileExistsOnRemoteByUniqueId(id, targetDrive.Alias, uid, WebOdinContext);
        }
    }

    /// <summary>
    /// Read one file on a peer drive named by slug, addressed by GlobalTransitId — the twin of
    /// <c>V2DrivePeerQueryByGtidController</c>.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppByGtid)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDriveQueryByGtidController(PeerDriveQueryService peerDriveQueryService)
        : V2PeerAppDriveControllerBase(peerDriveQueryService)
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            return await peerDriveQueryService.FileExistsOnRemoteByGlobalTransitId(id, targetDrive.Alias, gtid,
                WebOdinContext);
        }

        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var result = await peerDriveQueryService.GetFileHeaderByGlobalTransitIdAsync(
                id, ToGtidFile(targetDrive, gtid), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return result == null ? NotFound() : new JsonResult(result);
        }

        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey)
        {
            return GetPayloadInternal(odinId, appSlug, driveSlug, gtid, payloadKey, GetChunk(null, null));
        }

        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> GetPayload(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return GetPayloadInternal(odinId, appSlug, driveSlug, gtid, payloadKey, GetChunk(start, length));
        }

        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> GetThumbnail(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid,
            [FromRoute] string payloadKey,
            [FromRoute] int width,
            [FromRoute] int height,
            [FromQuery] bool directMatchOnly = false)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetThumbnailByGlobalTransitIdAsync(id, ToGtidFile(targetDrive, gtid),
                    payloadKey, width, height, directMatchOnly, GetHttpFileSystemResolver().GetFileSystemType(),
                    WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, string appSlug, string driveSlug,
            Guid gtid, string payloadKey, FileChunk chunk)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, payloadStream) =
                await peerDriveQueryService.GetPayloadByGlobalTransitIdAsync(id, ToGtidFile(targetDrive, gtid),
                    payloadKey, chunk, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }
    }
}
