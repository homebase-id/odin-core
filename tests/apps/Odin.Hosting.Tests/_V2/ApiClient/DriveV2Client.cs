using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsync(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadAsync(ExternalFileIdentifier file, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        return await svc.GetPayload(file.FileId, file.TargetDrive.Alias, key, chunk?.Start ?? 0, chunk?.Length ?? 0, fileSystemType);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(ExternalFileIdentifier file, int width, int height, string payloadKey,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnail(file.FileId, file.TargetDrive.Alias, payloadKey, width, height, directMatchOnly,
            fileSystemType);
        return thumbnailResponse;
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatchAsync(QueryBatchRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetBatch(request);
    }
    
    public async Task<ApiResponse<QueryBatchResponse>> GetSmartBatchAsync(QueryBatchRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetSmartBatch(request);
    }
    
    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollectionAsync(QueryBatchCollectionRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetBatchCollection(request);
    }
    
    public async Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistoryAsync(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalDriveHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.GetTransferHistory(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
        return apiResponse;
    }

    public async Task<ApiResponse<DriveStatus>> GetDriveStatusAsync(Guid driveId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStatusHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetDriveStatus(driveId);
        return apiResponse;
    }
}