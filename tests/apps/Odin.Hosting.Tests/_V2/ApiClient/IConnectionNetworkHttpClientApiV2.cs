using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Connections;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IConnectionNetworkHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Connections;

    [Get(Root + "/circles/with-members")]
    Task<ApiResponse<List<CircleWithMembers>>> GetCirclesWithMembers(bool includeSystemCircle = true);
}
