﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base.Drive;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveHttpClientApi
    {
        private const string RootStorageEndpoint = "/drive/files";
        private const string RootQueryEndpoint = "/drive/query";
        private const string RootSpecializedEndPoint = "/drive/query/specialized/cuid";
        
        [Post(RootSpecializedEndPoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueId(ClientUniqueIdFileIdentifier request);
        
        [Post(RootSpecializedEndPoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStreamByUniqueId([Body] GetThumbnailByUniqueIdRequest request);

        [Post(RootSpecializedEndPoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStreamByUniqueId([Body] GetPayloadByUniqueIdRequest request);
        
        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> UploadStream(StreamPart[] streamdata);

        [Multipart]
        [Post(RootStorageEndpoint + "/uploadpayload")]
        Task<ApiResponse<UploadPayloadResult>> UploadPayload(StreamPart[] streamdata);

        [Post(RootStorageEndpoint + "/delete")]
        Task<ApiResponse<DeleteFileResult>> DeleteFile([Body] DeleteFileRequest file);

        [Post(RootStorageEndpoint + "/deletefileidbatch")]
        Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileIdBatch([Body] DeleteFileIdBatchRequest request);

        [Post(RootStorageEndpoint + "/deletegroupidbatch")]
        Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdBatch([Body] DeleteFilesByGroupIdBatchRequest request);
        
        [Post(RootStorageEndpoint + "/deletepayload")]
        Task<ApiResponse<DeletePayloadResult>> DeletePayload([Body] DeletePayloadRequest request);

        [Post(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost([Body]ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadPost([Body]GetPayloadRequest request);

        [Post(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailPost([Body]GetThumbnailRequest request);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootQueryEndpoint + "/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified([Body]QueryModifiedRequest request);

        [Post(RootQueryEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch([Body]QueryBatchRequest request);

        [Post(RootQueryEndpoint + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([Body]QueryBatchCollectionRequest request);
    }
}