using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Connections;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IConnectionNetworkHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Connections;

    [Get(Root + "/circles/with-members")]
    Task<ApiResponse<List<CircleWithMembers>>> GetCirclesWithMembers(bool includeSystemCircle = true);

    [Post(Root + "/block")]
    Task<ApiResponse<HttpContent>> Block([Body] OdinIdRequest request);

    [Post(Root + "/unblock")]
    Task<ApiResponse<HttpContent>> Unblock([Body] OdinIdRequest request);

    [Post(Root + "/disconnect")]
    Task<ApiResponse<HttpContent>> Disconnect([Body] OdinIdRequest request);
}
