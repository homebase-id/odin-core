using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DrivesV1;

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeaderAsPost(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadAsPost(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailAsPost(GetThumbnailRequest request);

        [Get(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type);

        [Get(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

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