using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.UnifiedV2.Drive.Read;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveReaderV2Client(OdinId identity, IApiClientFactory factory)
{
    public SensitiveByteArray GetSharedSecret()
    {
        return factory.SharedSecret;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsync(Guid driveId, Guid fileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveReaderHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(driveId, fileId, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadAsync(Guid driveId, Guid fileId, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveReaderHttpClientApiV2>(client, sharedSecret);
        if (chunk == null)
        {
            return await svc.GetPayload(driveId, fileId, key, fileSystemType);

        }
        
        return await svc.GetPayload(driveId, fileId, key, chunk.Start, chunk.Length , fileSystemType);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(Guid driveId, Guid fileId, string payloadKey, int width, int height,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveReaderHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnail(driveId, fileId, payloadKey, width, height, directMatchOnly,
            fileSystemType);
        return thumbnailResponse;
    }
    
    // 

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(Guid driveId, Guid fileId, string payloadKey, 
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveReaderHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnail(driveId, fileId, payloadKey, null, null, null, fileSystemType);
        return thumbnailResponse;
    }
    
    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueIdAsync(Guid uid, Guid driveId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeaderByUniqueId(driveId, uid, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadByUniqueIdAsync(Guid uid, Guid driveId, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        if(chunk == null)
        {
            return await svc.GetPayloadByUniqueId(driveId, uid, key, fileSystemType);
        }
        return await svc.GetPayloadByUniqueId(driveId, uid, key, chunk?.Start ?? 0, chunk?.Length ?? 0, fileSystemType);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailUniqueIdAsync(Guid uid, Guid driveId, int width, int height, string payloadKey,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnailByUniqueId(driveId, uid, payloadKey, width, height, directMatchOnly,
            fileSystemType);
        return thumbnailResponse;
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatchAsync(Guid driveId, QueryBatchRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetBatch(driveId, request);
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetSmartBatchAsync(Guid driveId, QueryBatchRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetSmartBatch(driveId, request);
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollectionAsync(QueryBatchCollectionRequestV2 request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetBatchCollection(request);
    }

    public async Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistoryAsync(Guid driveId, Guid fileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveReaderHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetTransferHistory(driveId, fileId, fileSystemType);
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