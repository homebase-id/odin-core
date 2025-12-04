using System;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveQueryHttpClientApiV2
{
    private const string RootQueryEndpoint = UnifiedApiRouteConstants.FilesRoot;

    [Post(RootQueryEndpoint + "/query-batch")]
    Task<ApiResponse<QueryBatchResponse>> GetBatch([AliasAs("driveId:guid")] Guid driveId, [Body] QueryBatchRequest request);

    [Post(RootQueryEndpoint + "/query-batch-collection")]
    Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([AliasAs("driveId:guid")] Guid driveId,
        [Body] QueryBatchCollectionRequest request);

    [Post(RootQueryEndpoint + "/query-smart-batch")]
    Task<ApiResponse<QueryBatchResponse>> GetSmartBatch([AliasAs("driveId:guid")] Guid driveId, [Body] QueryBatchRequest request);
}