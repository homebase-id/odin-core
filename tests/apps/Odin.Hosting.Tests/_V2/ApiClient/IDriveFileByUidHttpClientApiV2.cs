using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveFileByUidHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ByUniqueId;

    [Get(Endpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload")]
    Task<ApiResponse<HttpContent>> GetPayload([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid, string key, int start, int length, FileSystemType fileSystemType);

    [Get(Endpoint + "/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnail([AliasAs("driveId:guid")] Guid driveId, [AliasAs("uid:guid")] Guid uid, string payloadKey, int width, int height, bool directMatchOnly,
        FileSystemType fileSystemType);
}