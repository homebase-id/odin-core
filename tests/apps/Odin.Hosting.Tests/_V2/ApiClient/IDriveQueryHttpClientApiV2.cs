using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveQueryHttpClientApiV2
{
    private const string RootQueryEndpoint = UnifiedApiRouteConstants.Query;

    [Post(RootQueryEndpoint + "/batch")]
    Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] QueryBatchRequest request);

    [Post(RootQueryEndpoint + "/batch-collection")]
    Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([Body] QueryBatchCollectionRequest request);
    
    [Post(RootQueryEndpoint + "/smart-batch")]
    Task<ApiResponse<QueryBatchResponse>> GetSmartBatch([Body] QueryBatchRequest request);
    
}