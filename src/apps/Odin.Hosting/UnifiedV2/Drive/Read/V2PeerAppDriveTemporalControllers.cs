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
        /// <summary>Checks whether you currently hold temporal access to a peer drive named by slug.</summary>
        /// <remarks>
        /// A preflight that reads no data, so an app can show a live "you have access" indicator without
        /// tripping the owner's notification for an actual read.
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        [HttpPost("verify")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        public async Task<TemporalAccessStatus> VerifyTemporalAccess(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            return await peerDriveQueryService.VerifyTemporalAccessAsync(id, targetDrive, fst, WebOdinContext);
        }

        /// <summary>Runs a time-clamped query against a peer drive named by slug.</summary>
        /// <remarks>
        /// Same request body as the ordinary peer query-batch, but the remote clamps results to a recent
        /// window regardless of what you ask for.
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        /// <param name="request">Query params and result options, same body as the ordinary query-batch.</param>
        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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
        /// <summary>Temporal read of a file header from a peer drive named by slug.</summary>
        /// <remarks>
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        /// <param name="fileId">The file's id <b>on the remote identity</b>.</param>
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Temporal read of a whole payload from a peer drive named by slug.</summary>
        /// <remarks>
        /// Honors the HTTP <c>Range</c> header.  Response is not shared-secret encrypted.
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload to read, from the file's manifest.</param>
        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Temporal read of a byte range of a payload from a peer drive named by slug.</summary>
        /// <remarks>
        /// The route-based alternative to a <c>Range</c> header.
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload to read.</param>
        /// <param name="start">First byte to return, zero-based.</param>
        /// <param name="length">How many bytes to return.</param>
        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Temporal read of a payload thumbnail from a peer drive named by slug.</summary>
        /// <remarks>
        /// Returns the closest thumbnail at or above the requested size.
        /// <para><b>Temporal reads are not ordinary reads.</b>  Your access comes from
        /// <c>DrivePermission.ConditionalTemporalRead</c> granted through a circle; the remote clamps every
        /// result to a recent window and notifies its owner that you read.  Do not use these as a substitute
        /// for the normal read routes.</para>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need no
        /// guid constants shared with them.  400 when nothing there answers to the address.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>.</param>
        /// <param name="driveSlug">The drive's slug within that app.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload the thumbnail belongs to.</param>
        /// <param name="width">Requested width in pixels.</param>
        /// <param name="height">Requested height in pixels.</param>
        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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
