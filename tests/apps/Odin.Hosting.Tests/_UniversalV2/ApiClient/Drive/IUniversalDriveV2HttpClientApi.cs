using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.APIv2;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests._UniversalV2.ApiClient.Drive
{
    public interface IUniversalDriveV2HttpClientApi
    {
        private const string Root = "/";
        private const string RootStorageEndpoint = Root;
        private const string RootQueryEndpoint = Root + "/query";

        [Post("/transit/inbox/processor/process")]
        Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);

        [Multipart]
        [Post(Root + ApiV2PathConstants.CreateFile)]
        Task<ApiResponse<UploadResult>> CreateFile(StreamPart[] streamdata);


        [Delete(Root + ApiV2PathConstants.DeleteFile)]
        Task<ApiResponse<DeleteFileResult>> SoftDeleteFile([Body] DeleteFileRequest file);

        [Post(RootStorageEndpoint + "/deletefileidbatch")]
        Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileIdBatch([Body] DeleteFileIdBatchRequest request);

        [Post(RootStorageEndpoint + "/deletegroupidbatch")]
        Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdBatch([Body] DeleteFilesByGroupIdBatchRequest request);

        [Delete(Root + ApiV2PathConstants.DeletePayload)]
        Task<ApiResponse<DeletePayloadResult>> DeletePayload([Body] DeletePayloadRequest request);

        [Post(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost([Body] ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadPost([Body] GetPayloadRequest request);

        [Post(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailPost([Body] GetThumbnailRequest request);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootQueryEndpoint + "/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified([Body] QueryModifiedRequest request);

        [Post(RootQueryEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] QueryBatchRequest request);

        [Post(RootQueryEndpoint + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([Body] QueryBatchCollectionRequest request);

        [Get(Root + "/status")]
        Task<ApiResponse<DriveStatus>> GetDriveStatus(Guid alias, Guid type);

        [Post(RootStorageEndpoint + "/send-read-receipt")]
        Task<ApiResponse<SendReadReceiptResult>> SendReadReceipt(SendReadReceiptRequest request);
    }
}