using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.LiveRelay;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// Client for the V2 live-relay endpoint (hop 1: app -> its own server).
/// </summary>
public interface ILiveRelayHttpClientApiV2
{
    [Post(UnifiedApiRouteConstants.LiveRelay)]
    Task<ApiResponse<HttpContent>> Relay([Body] LiveRelayRequest request);
}
