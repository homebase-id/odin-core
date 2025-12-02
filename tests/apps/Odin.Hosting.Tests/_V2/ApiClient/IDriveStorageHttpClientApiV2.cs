using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveStorageHttpClientApiV2
{
    private const string RootStorageEndpoint = UnifiedApiRouteConstants.Files;

    [Get(RootStorageEndpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid driveId, FileSystemType fileSystemType);

    [Get(RootStorageEndpoint + "/payload")]
    Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid driveId, string key, int start, int length, FileSystemType fileSystemType);

    [Get(RootStorageEndpoint + "/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid driveId, string payloadKey, int width, int height, bool directMatchOnly, FileSystemType fileSystemType);

    [Get(RootStorageEndpoint + "/transfer-history")]
    Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistory(Guid fileId, Guid driveId);
    
    // add create, update, delete here
}