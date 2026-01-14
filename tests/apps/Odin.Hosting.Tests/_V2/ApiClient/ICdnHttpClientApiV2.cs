using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface ICdnHttpClientApiV2
{
    [Get(UnifiedApiRouteConstants.DrivesRoot + "/cdn-ping/payload/{size:int}")]
    Task<ApiResponse<HttpContent>> CdnPing([AliasAs("size:int")]int size);

    [Get(UnifiedApiRouteConstants.DrivesRoot + "/cdn-ping/bad-cdn-path")]
    Task<ApiResponse<HttpContent>> CdnPingBadPath();
}
