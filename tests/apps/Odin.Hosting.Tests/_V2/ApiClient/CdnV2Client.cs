using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class CdnV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> CdnPing(int size)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<ICdnHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.CdnPing(size);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> CdnPingBadPath()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<ICdnHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.CdnPingBadPath();
        return apiResponse;
    }

}
