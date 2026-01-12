using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface ICdnHttpClientApiV2
{
    [Get(UnifiedApiRouteConstants.DrivesRoot + "/cdn-ping/payload/cdn-ping")]
    Task<ApiResponse<HttpContent>> CdnPing();

    [Get(UnifiedApiRouteConstants.DrivesRoot + "/cdn-ping/bad-cdn-path/cdn-ping")]
    Task<ApiResponse<HttpContent>> CdnPingBadPath();
}
