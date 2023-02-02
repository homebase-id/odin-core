using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.DrivesV1;
        private const string RootQueryEndpoint = OwnerApiPathConstants.DriveQueryV1;
        private const string RootStorageEndpoint = OwnerApiPathConstants.DriveStorageV1;

        
        [Multipart]
        [Post(RootStorageEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);
        
        [Post(RootStorageEndpoint + "/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(ExternalFileIdentifier file);

        [Post(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file);

        
        [Post(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(GetThumbnailRequest request);

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