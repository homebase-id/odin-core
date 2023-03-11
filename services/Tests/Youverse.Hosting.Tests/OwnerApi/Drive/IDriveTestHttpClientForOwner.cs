using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForOwner
    {
        private const string RootQueryEndpoint = OwnerApiPathConstants.DriveQueryV1;
        private const string RootStorageEndpoint = OwnerApiPathConstants.DriveStorageV1;

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> UploadStream(StreamPart[] thumbnail);

        [Post(RootStorageEndpoint + "/delete")]
        Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile([Body] DeleteFileRequest file);

        [Post(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost(ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadPost(ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailPost(GetThumbnailRequest request);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootQueryEndpoint + "/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootQueryEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
        

        [Post(RootQueryEndpoint + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(QueryBatchCollectionRequest request);

        [Post(OwnerApiPathConstants.TransitV1 + "/outbox/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox(int batchSize);

        [Post(OwnerApiPathConstants.TransitV1 + "/inbox/processor/process")]
        Task<ApiResponse<bool>> ProcessIncomingInstructions([Body] ProcessTransitInstructionRequest request);
    }
}