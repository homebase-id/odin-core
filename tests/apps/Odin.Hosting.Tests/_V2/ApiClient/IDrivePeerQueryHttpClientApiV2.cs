using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDrivePeerQueryHttpClientApiV2
{
    [Get(UnifiedApiRouteConstants.PeerByUniqueId + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByUid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("uid:guid")] Guid uid);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("start:int")] int start,
        [AliasAs("length:int")] int length);

    [Get(UnifiedApiRouteConstants.PeerByGtid + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> GetThumbnailByGtid(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("width")] int width,
        [AliasAs("height")] int height);

    [Post(UnifiedApiRouteConstants.PeerByDriveId + "/query-batch")]
    Task<ApiResponse<QueryBatchResponse>> QueryBatch(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [Body] QueryBatchRequest request);

    [Get(UnifiedApiRouteConstants.PeerByFileId + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId);

    // --- Temporal (time-boxed) read API ---

    [Post(UnifiedApiRouteConstants.PeerTemporalRoot + "/verify")]
    Task<ApiResponse<TemporalAccessStatus>> VerifyTemporalAccess(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId);

    [Get(UnifiedApiRouteConstants.PeerTemporalByFileId + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> TemporalGetFileHeader(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId);

    [Get(UnifiedApiRouteConstants.PeerByFileId + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayload(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [AliasAs("payloadKey")] string payloadKey);

    [Get(UnifiedApiRouteConstants.PeerByFileId + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayload(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("start:int")] int start,
        [AliasAs("length:int")] int length);

    [Get(UnifiedApiRouteConstants.PeerByFileId + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> GetThumbnail(
        [AliasAs("odinId")] string odinId,
        [AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("width")] int width,
        [AliasAs("height")] int height);
}
