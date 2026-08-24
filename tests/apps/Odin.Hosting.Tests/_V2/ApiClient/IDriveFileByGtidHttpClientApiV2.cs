using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveFileByGtidHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ByGtid;

    [Get(Endpoint + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid([AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("start:int")] int start,
        [AliasAs("length:int")] int length,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayloadByGtid([AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnailByGtid([AliasAs("driveId:guid")] Guid driveId, [AliasAs("gtid:guid")] Guid gtid,
        [AliasAs("payloadKey")] string payloadKey,
        int width,
        int height,
        bool directMatchOnly,
        FileSystemType fileSystemType);
}
