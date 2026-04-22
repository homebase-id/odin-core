using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IConnectionRequestsHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Connections;

    [Post(Root + "/requests/auto-connect")]
    Task<ApiResponse<AutoConnectResult>> AutoConnect([Body] ConnectionRequestHeader header);
}
