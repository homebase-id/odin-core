using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
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
        Task<ApiResponse<UploadResult>> Upload([AliasAs("instructions")] StreamPart instructionSet, [AliasAs("metaData")] StreamPart metaData, [AliasAs("payload")] StreamPart payload);

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(TargetDrive drive, Guid fileId);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(TargetDrive drive, Guid fileId);

        [Post(RootQueryEndpoint + "/recent")]
        Task<ApiResponse<QueryBatchResult>> GetRecent([Query] TargetDrive drive, [Query] UInt64 maxDate, [Query] byte[] startCursor, [Body] QueryParams qp, [Query] ResultOptions options);

        [Post(RootQueryEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResult>> GetBatch([Body] QueryParams qp, [Query] GetBatchQueryResultOptions options);

        [Post(OwnerApiPathConstants.TransitV1 + "/outbox/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox();
    }
}