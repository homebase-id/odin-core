using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveReactionHttpTestClientForOwner
    {
        private const string RootStorageEndpoint = OwnerApiPathConstants.DriveCommentsV1;

        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> UploadComment(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<ClientFileHeader>> GetCommentFileHeader(Guid reactionFileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetCommentPayload(Guid reactionFileId, Guid alias, Guid type);

        [Get(RootStorageEndpoint + "/list")]
        Task<ApiResponse<List<ClientFileHeader>>> GetTextReactionsByReferenceFile(Guid referencedFileId, Guid alias, Guid type, int pageNumber, int pageSize);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetCommentThumbnail(Guid reactionFileId,  Guid alias, Guid type, int width, int height);

        // [Post(RootQueryEndpoint + "/modified")]
        // Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);
        //
        // [Post(RootQueryEndpoint + "/batch")]
        // Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
        //
        // [Post(RootQueryEndpoint + "/batchcollection")]
        // Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(QueryBatchCollectionRequest request);
    }
}