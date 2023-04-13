using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DrivesV1;
        private const string ReactionRootEndpoint = AppApiPathConstants.DriveReactionsV1;

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

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
        Task<ApiResponse<QueryModifiedResult>> QueryModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch(QueryBatchRequest request);
        
        [Post(RootEndpoint + "/query/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(QueryBatchCollectionRequest request);

        [Post(RootEndpoint + "/files/delete")]
        Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile([Body] DeleteFileRequest file);
        
        
    }
}