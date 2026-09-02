using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// The slug-addressed peer routes: <c>/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/…</c>.
/// Mirrors <see cref="IDrivePeerQueryHttpClientApiV2"/> and
/// <see cref="IDrivePeerWriteHttpClientApiV2"/> action for action, so a test can be written twice --
/// once per addressing form -- and the two compared.
/// </summary>
public interface IPeerAppDriveHttpClientApiV2
{
    private const string Drive = UnifiedApiRouteConstants.PeerAppDriveBySlug;
    private const string ByFileId = UnifiedApiRouteConstants.PeerAppByFileId;
    private const string ByUid = UnifiedApiRouteConstants.PeerAppByUniqueId;
    private const string ByGtid = UnifiedApiRouteConstants.PeerAppByGtid;
    private const string Temporal = UnifiedApiRouteConstants.PeerAppTemporalRoot;
    private const string TemporalByFileId = UnifiedApiRouteConstants.PeerAppTemporalByFileId;

    // ---- query ----

    [Post(Drive + "/query-batch")]
    Task<ApiResponse<QueryBatchResponse>> QueryBatch(string odinId, string appSlug, string driveSlug,
        [Body] QueryBatchRequestV2 request);

    // ---- by FileId ----

    [Get(ByFileId + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId);

    [Get(ByFileId + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayload(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey);

    [Get(ByFileId + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayload(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey,
        [AliasAs("start:int")] int start, [AliasAs("length:int")] int length);

    [Get(ByFileId + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> GetThumbnail(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey, int width, int height);

    // ---- by UniqueId / GlobalTransitId ----

    [Get(ByUid + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByUid(string odinId, string appSlug, string driveSlug,
        [AliasAs("uid:guid")] Guid uid);

    [Get(ByGtid + "/exists")]
    Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByGtid(string odinId, string appSlug, string driveSlug,
        [AliasAs("gtid:guid")] Guid gtid);

    [Get(ByGtid + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByGtid(string odinId, string appSlug,
        string driveSlug, [AliasAs("gtid:guid")] Guid gtid);

    [Get(ByGtid + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid(string odinId, string appSlug, string driveSlug,
        [AliasAs("gtid:guid")] Guid gtid, string payloadKey);

    [Get(ByGtid + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid(string odinId, string appSlug, string driveSlug,
        [AliasAs("gtid:guid")] Guid gtid, string payloadKey,
        [AliasAs("start:int")] int start, [AliasAs("length:int")] int length);

    [Get(ByGtid + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> GetThumbnailByGtid(string odinId, string appSlug, string driveSlug,
        [AliasAs("gtid:guid")] Guid gtid, string payloadKey, int width, int height);

    // ---- temporal ----

    [Post(Temporal + "/verify")]
    Task<ApiResponse<TemporalAccessStatus>> VerifyTemporalAccess(string odinId, string appSlug, string driveSlug);

    [Post(Temporal + "/query-batch")]
    Task<ApiResponse<QueryBatchResponse>> TemporalQueryBatch(string odinId, string appSlug, string driveSlug,
        [Body] QueryBatchRequestV2 request);

    [Get(TemporalByFileId + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> TemporalGetFileHeader(string odinId, string appSlug,
        string driveSlug, [AliasAs("fileId:guid")] Guid fileId);

    [Get(TemporalByFileId + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> TemporalGetPayload(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey);

    [Get(TemporalByFileId + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> TemporalGetPayload(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey,
        [AliasAs("start:int")] int start, [AliasAs("length:int")] int length);

    [Get(TemporalByFileId + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> TemporalGetThumbnail(string odinId, string appSlug, string driveSlug,
        [AliasAs("fileId:guid")] Guid fileId, string payloadKey, int width, int height);

    // ---- write ----

    [Multipart]
    [Post(Drive + "/files/send")]
    Task<ApiResponse<TransitResult>> SendFile(string odinId, string appSlug, string driveSlug,
        StreamPart[] streamdata);

    [Post(Drive + "/files/senddeleterequest")]
    Task<ApiResponse<Dictionary<string, DeleteLinkedFileStatus>>> SendDeleteRequest(string odinId, string appSlug,
        string driveSlug, [Body] DeleteFileByGlobalTransitIdRequest request);
}
