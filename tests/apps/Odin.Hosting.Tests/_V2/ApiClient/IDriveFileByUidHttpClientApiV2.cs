using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveFileByUidHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ByUniqueId;

    [Get(Endpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueId([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("uid:guid")] Guid uid,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayloadByUniqueId([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("start:int")] int start,
        [AliasAs("length:int")] int length,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayloadByUniqueId([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnailByUniqueId([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        int width,
        int height,
        bool directMatchOnly,
        FileSystemType fileSystemType);
}