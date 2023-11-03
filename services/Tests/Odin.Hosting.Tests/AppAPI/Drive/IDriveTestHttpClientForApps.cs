using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Core.Services.Peer;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DriveV1;
        private const string ReactionRootEndpoint = AppApiPathConstants.DriveReactionsV1;

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart[] streamdata);
        
        [Multipart]
        [Post(RootEndpoint + "/files/attachments/upload")]
        Task<ApiResponse<UploadAttachmentsResult>> UploadAttachments(StreamPart[] streamdata);

        
        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadAsPost(GetPayloadRequest request);

        [Post(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailAsPost(GetThumbnailRequest request);

        [Get(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type, long? offsetPosition);

        [Get(RootEndpoint + "/files/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootEndpoint + "/query/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
        
        [Post(RootEndpoint + "/query/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(QueryBatchCollectionRequest request);

        [Post(RootEndpoint + "/files/delete")]
        Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile([Body] DeleteFileRequest request);
        
        [Post(RootEndpoint + "/files/attachments/deletethumbnail")]
        Task<ApiResponse<DeleteAttachmentsResult>> DeleteThumbnail([Body] DeleteThumbnailRequest request);

        [Post(RootEndpoint + "/files/attachments/deletepayload")]
        Task<ApiResponse<DeleteAttachmentsResult>> DeletePayload([Body] DeletePayloadRequest request);
        
    }
}