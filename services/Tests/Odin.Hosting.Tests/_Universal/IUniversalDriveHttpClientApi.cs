﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Core.Services.Peer;
using Odin.Hosting.Controllers.Base.Drive;
using Refit;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests._Universal
{
    public interface IUniversalDriveHttpClientApi
    {
        private const string RootStorageEndpoint = "/drive/files";

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> UploadStream(StreamPart[] streamdata);

        [Multipart]
        [Post(RootStorageEndpoint + "/uploadpayload")]
        Task<ApiResponse<UploadPayloadResult>> UploadPayload(StreamPart[] streamdata);

        [Post(RootStorageEndpoint + "/delete")]
        Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile([Body] DeleteFileRequest file);

        [Post(RootStorageEndpoint + "/deletepayload")]
        Task<ApiResponse<DeletePayloadResult>> DeletePayload([Body] DeletePayloadRequest request);

        [Post(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost(ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadPost(GetPayloadRequest request);

        [Post(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailPost(GetThumbnailRequest request);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootStorageEndpoint + "/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootStorageEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);

        [Post(RootStorageEndpoint + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(QueryBatchCollectionRequest request);
    }
}