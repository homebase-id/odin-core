using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveReaderHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ByFileId;

    [Get(Endpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, FileSystemType fileSystemType);

    [Get(Endpoint + "/payload")]
    Task<ApiResponse<HttpContent>> GetPayload([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid fileId, string key,
        int start, int length, FileSystemType fileSystemType);

    [Get(Endpoint + "/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnail([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid fileId,
        string payloadKey, int width, int height, bool directMatchOnly, FileSystemType fileSystemType);

    [Get(Endpoint + "/transfer-history")]
    Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistory([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, FileSystemType fileSystemType);
}