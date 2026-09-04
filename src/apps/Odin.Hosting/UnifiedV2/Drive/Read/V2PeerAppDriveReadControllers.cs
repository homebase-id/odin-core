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
        /// <summary>Queries files on a peer drive named by slug.</summary>
        /// <remarks>
        /// The slug-addressed twin of <c>POST /peer/{odinId}/drives/{driveId}/query-batch</c>.  Use it to
        /// catch up on a drive owned by another identity — a collaborative community drive, say.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="request">Query params and result options, same body as the guid route.</param>
        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

            var batch = await PeerQuery.GetBatchAsync(id, v1Request, fst, WebOdinContext);
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
        /// <summary>Reads a file header from a peer drive named by slug, by FileId.</summary>
        /// <remarks>
        /// FileId is assigned by the host that stores the file, so it is only meaningful on
        /// <paramref name="odinId"/>'s identity.  If you are working from a file you sent, you want the
        /// by-gtid routes instead — your copy and theirs have different FileIds.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="fileId">The file's id <b>on the remote identity</b>.</param>
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid fileId)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var result = await PeerQuery.GetFileHeaderAsync(
                id, ToExternalFile(targetDrive, fileId), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return result == null ? NotFound() : new JsonResult(result);
        }

        /// <summary>Reads a whole payload from a peer drive named by slug, by FileId.</summary>
        /// <remarks>
        /// Honors the HTTP <c>Range</c> header.  Response is not shared-secret encrypted.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload to read, from the file's manifest.</param>
        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Reads a byte range of a payload from a peer drive named by slug, by FileId.</summary>
        /// <remarks>
        /// The route-based alternative to a <c>Range</c> header, for clients that cannot set one.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload to read.</param>
        /// <param name="start">First byte to return, zero-based.</param>
        /// <param name="length">How many bytes to return.</param>
        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Reads a payload thumbnail from a peer drive named by slug, by FileId.</summary>
        /// <remarks>
        /// Returns the closest thumbnail at or above the requested size.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="fileId">The file's id on the remote identity.</param>
        /// <param name="payloadKey">Which payload the thumbnail belongs to.</param>
        /// <param name="width">Requested width in pixels.</param>
        /// <param name="height">Requested height in pixels.</param>
        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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
                await PeerQuery.GetThumbnailAsync(id, ToExternalFile(targetDrive, fileId), width, height,
                    payloadKey, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, string appSlug, string driveSlug,
            Guid fileId, string payloadKey, FileChunk chunk)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, payloadStream) = await PeerQuery.GetPayloadStreamAsync(
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
        /// <summary>Checks whether a file with the given UniqueId exists on a peer drive named by slug.</summary>
        /// <remarks>
        /// UniqueId is client-assigned and travels with the file, so unlike FileId it means the same thing on
        /// both identities.  Useful for "have they got this yet" without transferring the file.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="uid">The client-assigned unique id to look for.</param>
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid uid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            return await PeerQuery.FileExistsOnRemoteByUniqueId(id, targetDrive.Alias, uid, WebOdinContext);
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
        /// <summary>Checks whether a file with the given GlobalTransitId exists on a peer drive named by slug.</summary>
        /// <remarks>
        /// GlobalTransitId is assigned at send time and is the same on sender and recipient, which makes it the
        /// right handle for "did my file arrive".
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="gtid">The GlobalTransitId to look for.</param>
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);
            return await PeerQuery.FileExistsOnRemoteByGlobalTransitId(id, targetDrive.Alias, gtid,
                WebOdinContext);
        }

        /// <summary>Reads a file header from a peer drive named by slug, by GlobalTransitId.</summary>
        /// <remarks>
        /// Prefer this over the by-FileId form when you sent the file: the recipient stores it under a different
        /// FileId, but the same GlobalTransitId.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="gtid">The file's GlobalTransitId.</param>
        [HttpGet("header")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        public async Task<IActionResult> GetFileHeader(
            [FromRoute] string odinId,
            [FromRoute] string appSlug,
            [FromRoute] string driveSlug,
            [FromRoute] Guid gtid)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var result = await PeerQuery.GetFileHeaderByGlobalTransitIdAsync(
                id, ToGtidFile(targetDrive, gtid), GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return result == null ? NotFound() : new JsonResult(result);
        }

        /// <summary>Reads a whole payload from a peer drive named by slug, by GlobalTransitId.</summary>
        /// <remarks>
        /// Honors the HTTP <c>Range</c> header.  Response is not shared-secret encrypted.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="gtid">The file's GlobalTransitId.</param>
        /// <param name="payloadKey">Which payload to read, from the file's manifest.</param>
        [HttpGet("payload/{payloadKey}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Reads a byte range of a payload from a peer drive named by slug, by GlobalTransitId.</summary>
        /// <remarks>
        /// The route-based alternative to a <c>Range</c> header.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="gtid">The file's GlobalTransitId.</param>
        /// <param name="payloadKey">Which payload to read.</param>
        /// <param name="start">First byte to return, zero-based.</param>
        /// <param name="length">How many bytes to return.</param>
        [HttpGet("payload/{payloadKey}/{start:int}/{length:int}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

        /// <summary>Reads a payload thumbnail from a peer drive named by slug, by GlobalTransitId.</summary>
        /// <remarks>
        /// Returns the closest thumbnail at or above the requested size, unless
        /// <paramref name="directMatchOnly"/> is set.
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them.  They resolve the address; you need a grant on whatever it
        /// resolves to, exactly as with the guid routes.</para>
        /// <para>400 when nothing there answers to the address — unknown app, unknown drive, and a drive you
        /// may not access are deliberately indistinguishable.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        /// <param name="gtid">The file's GlobalTransitId.</param>
        /// <param name="payloadKey">Which payload the thumbnail belongs to.</param>
        /// <param name="width">Requested width in pixels.</param>
        /// <param name="height">Requested height in pixels.</param>
        /// <param name="directMatchOnly">When true, return only an exact size match rather than the nearest.</param>
        [HttpGet("payload/{payloadKey}/thumb/{width}/{height}")]
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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
                await PeerQuery.GetThumbnailByGlobalTransitIdAsync(id, ToGtidFile(targetDrive, gtid),
                    payloadKey, width, height, directMatchOnly, GetHttpFileSystemResolver().GetFileSystemType(),
                    WebOdinContext);

            return HandlePeerThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private async Task<IActionResult> GetPayloadInternal(string odinId, string appSlug, string driveSlug,
            Guid gtid, string payloadKey, FileChunk chunk)
        {
            var (id, targetDrive) = await ResolveAsync(odinId, appSlug, driveSlug);

            var (encryptedKeyHeader, isEncrypted, payloadStream) =
                await PeerQuery.GetPayloadByGlobalTransitIdAsync(id, ToGtidFile(targetDrive, gtid),
                    payloadKey, chunk, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext);

            return HandlePeerPayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }
    }
}
