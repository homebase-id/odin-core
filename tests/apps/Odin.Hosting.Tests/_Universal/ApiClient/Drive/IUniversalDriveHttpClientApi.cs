using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Odin.Core.Identity;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests._Universal.ApiClient.Drive
{
    public interface IUniversalDriveHttpClientApi
    {
        private const string RootDriveEndpoint = "/drive";
        private const string RootStorageEndpoint = RootDriveEndpoint + "/files";
        private const string RootQueryEndpoint = RootDriveEndpoint + "/query";

        [Post("/transit/inbox/processor/process")]
        Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> UploadStream(StreamPart[] streamdata);

        [Multipart]
        [Post(RootStorageEndpoint + "/uploadpayload")]
        Task<ApiResponse<UploadPayloadResult>> UploadPayload(StreamPart[] streamdata);

        [Multipart]
        [Patch(RootStorageEndpoint + "/update")]
        Task<ApiResponse<UploadPayloadResult>> UpdateFile(StreamPart[] streamdata);

        [Post(RootStorageEndpoint + "/delete")]
        Task<ApiResponse<DeleteFileResult>> SoftDeleteFile([Body] DeleteFileRequest file);

        [Post(RootStorageEndpoint + "/deletefileidbatch")]
        Task<ApiResponse<DeleteFileIdBatchResult>> DeleteFileIdBatch([Body] DeleteFileIdBatchRequest request);

        [Post(RootStorageEndpoint + "/deletegroupidbatch")]
        Task<ApiResponse<DeleteFilesByGroupIdBatchResult>> DeleteFilesByGroupIdBatch([Body] DeleteFilesByGroupIdBatchRequest request);

        [Post(RootStorageEndpoint + "/deletepayload")]
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

        [Get(RootDriveEndpoint + "/status")]
        Task<ApiResponse<DriveStatus>> GetDriveStatus(Guid alias, Guid type);

        [Get(RootDriveEndpoint + "/outbox-item")]
        Task<ApiResponse<RedactedOutboxFileItem>> GetOutboxItem(Guid alias, Guid type, Guid fileId, string recipient);

        [Post(RootStorageEndpoint + "/send-read-receipt")]
        Task<ApiResponse<SendReadReceiptResult>> SendReadReceipt(SendReadReceiptRequest request);
    }
}