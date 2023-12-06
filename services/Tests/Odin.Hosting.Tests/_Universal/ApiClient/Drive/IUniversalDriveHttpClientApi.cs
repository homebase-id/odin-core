using System;
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

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveHttpClientApi
    {
        private const string RootStorageEndpoint = "/drive/files";
        private const string RootQueryEndpoint = "/query";

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