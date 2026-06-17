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
    /// Temporal (time-boxed / "emergency") read of a drive hosted by another (peer) identity. The caller's
    /// access derives from <c>DrivePermission.ConditionalTemporalRead</c> granted via a circle; the remote
    /// clamps every read to a recent window and notifies its owner. Includes a lightweight <c>verify</c>
    /// preflight so an app can show a live "you have access" indicator without reading data.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByDriveId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerTemporalController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpPost("temporal/verify")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<TemporalAccessStatus> VerifyTemporalAccess(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId)
        {
            AssertIsValidOdinId(odinId, out var id);
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            return await peerDriveQueryService.VerifyTemporalAccessAsync(id, ToTargetDrive(driveId), fst, WebOdinContext);
        }

        [HttpPost("temporal/query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> TemporalQueryBatch(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromBody] QueryBatchRequestV2 request)
        {
            AssertIsValidOdinId(odinId, out var id);
            OdinValidationUtils.AssertNotNull(request, "request");
            OdinValidationUtils.AssertNotNull(request.QueryParams, "QueryParams");
            OdinValidationUtils.AssertNotNull(request.ResultOptionsRequest, "ResultOptionsRequest");

            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            var v1Request = new QueryBatchRequest
            {
                QueryParams = ToV1QueryParams(request.QueryParams, driveId),
                ResultOptionsRequest = request.ResultOptionsRequest,
                FileSystemType = fst
            };

            var batch = await peerDriveQueryService.GetTemporalBatchAsync(id, v1Request, fst, WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        [HttpGet("temporal/files/{fileId:guid}/header")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        public async Task<IActionResult> TemporalGetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId)
        {
            AssertIsValidOdinId(odinId, out var id);
            var result = await peerDriveQueryService.GetTemporalFileHeaderAsync(
                id, ToExternalFile(driveId, fileId), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet("temporal/files/{fileId:guid}/payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> TemporalGetPayload(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey)
        {
            return TemporalGetPayloadInternal(odinId, driveId, fileId, payloadKey, GetChunk(null, null));
        }

        [HttpGet("temporal/files/{fileId:guid}/payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public Task<IActionResult> TemporalGetPayload(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int start,
            [FromRoute] int length)
        {
            return TemporalGetPayloadInternal(odinId, driveId, fileId, payloadKey, GetChunk(start, length));
        }

        [HttpGet("temporal/files/{fileId:guid}/payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileRead])]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        public async Task<IActionResult> TemporalGetThumbnail(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid fileId,
            [FromRoute] string payloadKey,
            [FromRoute] int width,
            [FromRoute] int height)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetTemporalThumbnailAsync(id, ToExternalFile(driveId, fileId), width, height,
                    payloadKey, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> TemporalGetPayloadInternal(string odinId, Guid driveId, Guid fileId, string payloadKey,
            FileChunk chunk)
        {
            AssertIsValidOdinId(odinId, out var id);

            var (encryptedKeyHeader, isEncrypted, payloadStream) = await peerDriveQueryService.GetTemporalPayloadStreamAsync(
                id, ToExternalFile(driveId, fileId), payloadKey, chunk, GetHttpFileSystemResolver().GetFileSystemType(),
                WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }

        private static TargetDrive ToTargetDrive(Guid driveId)
        {
            return new TargetDrive { Alias = driveId, Type = Guid.Empty };
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

        private static FileQueryParamsV1 ToV1QueryParams(FileQueryParams p, Guid driveId)
        {
            return new FileQueryParamsV1
            {
                TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty },
                FileType = p.FileType,
                FileState = p.FileState,
                DataType = p.DataType,
                ArchivalStatus = p.ArchivalStatus,
                Sender = p.Sender,
                GroupId = p.GroupId,
                UserDate = p.UserDate,
                ClientUniqueIdAtLeastOne = p.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = p.TagsMatchAtLeastOne,
                TagsMatchAll = p.TagsMatchAll,
                LocalTagsMatchAtLeastOne = p.LocalTagsMatchAtLeastOne,
                LocalTagsMatchAll = p.LocalTagsMatchAll,
                GlobalTransitId = p.GlobalTransitId
            };
        }
    }
}
