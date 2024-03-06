using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Home.Auth;
using Refit;

namespace Odin.Hosting.Tests.BuiltIn.Home
{

    public interface IRefitHomeDriveQuery
    {
        private const string RootEndpoint = HomeApiPathConstants.CacheableV1;
        
        [Post(RootEndpoint + "/qbc")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection([Body] QueryBatchCollectionRequest request);

        [Post(RootEndpoint + "/invalidate")]
        Task<ApiResponse<HttpContent>> InvalidateCache();
    }
}