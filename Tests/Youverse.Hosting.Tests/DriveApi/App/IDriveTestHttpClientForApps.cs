using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.DriveApi.App
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DrivesV1;
        
        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(
            [AliasAs("instructions")] StreamPart instructionSet,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
        
        [Get(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(TargetDrive drive, Guid fileId);

        [Get(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(TargetDrive drive, Guid fileId);
        
        [Post(RootEndpoint + "/query/recent")]
        Task<ApiResponse<QueryBatchResult>> GetRecent([Query] TargetDrive drive, [Query] UInt64 maxDate, [Query] byte[] startCursor, [Body] QueryParams qp, [Query] ResultOptions options);
        
        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResult>> GetBatch([Body] QueryParams qp, [Query] GetBatchQueryResultOptions options);

        
    }
}