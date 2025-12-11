using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Encryption;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveV2Client(OdinId identity, IApiClientFactory factory)
{
    public SensitiveByteArray GetSharedSecret()
    {
        return factory.SharedSecret;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsync(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(file.TargetDrive.Alias, file.FileId, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadAsync(ExternalFileIdentifier file, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        return await svc.GetPayload(file.TargetDrive.Alias, file.FileId, key, chunk?.Start ?? 0, chunk?.Length ?? 0, fileSystemType);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(ExternalFileIdentifier file, int width, int height, string payloadKey,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnail(file.TargetDrive.Alias, file.FileId, payloadKey, width, height, directMatchOnly,
            fileSystemType);
        return thumbnailResponse;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueIdAsync(Guid uid, Guid driveId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(driveId, uid, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadByUniqueIdAsync(Guid uid, Guid driveId, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        return await svc.GetPayload(driveId, uid, key, chunk?.Start ?? 0, chunk?.Length ?? 0, fileSystemType);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailUniqueIdAsync(Guid uid, Guid driveId, int width, int height, string payloadKey,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveFileByUidHttpClientApiV2>(client, sharedSecret);
        var thumbnailResponse = await svc.GetThumbnail(driveId, uid, payloadKey, width, height, directMatchOnly,
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

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollectionAsync(Guid driveId, QueryBatchCollectionRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetBatchCollection(driveId, request);
    }

    public async Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistoryAsync(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetTransferHistory(file.TargetDrive.Alias, file.FileId, fileSystemType);
        return apiResponse;
    }

    public async Task<ApiResponse<DriveStatus>> GetDriveStatusAsync(Guid driveId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveStatusHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetDriveStatus(driveId);
        return apiResponse;
    }

    public async Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalAppMetadataTags(Guid driveId, Guid fileId,
        UpdateLocalMetadataTagsRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var driveSvc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
            ApiResponse<UpdateLocalMetadataResult> response = await driveSvc.UpdateLocalMetadataTags(driveId, fileId, request);

            keyHeader.AesKey.Wipe();

            return response;
        }
    }

    public async Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalAppMetadataContent(Guid driveId, Guid fileId,
        UpdateLocalMetadataContentRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        {
            var driveSvc = RefitCreator.RestServiceFor<IDriveStorageHttpClientApiV2>(client, sharedSecret);
            ApiResponse<UpdateLocalMetadataResult> response = await driveSvc.UpdateLocalMetadataContent(driveId, fileId, request);

            keyHeader.AesKey.Wipe();

            return response;
        }
    }
}