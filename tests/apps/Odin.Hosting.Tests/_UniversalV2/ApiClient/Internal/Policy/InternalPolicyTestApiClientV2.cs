using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._UniversalV2.ApiClient.Internal.Policy;

public class InternalPolicyTestApiClientV2(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> GetTest()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IInternalPolicyTestHttpClientApi>(client, sharedSecret);
        var apiResponse = await svc.GetTest();
        return apiResponse;
    }
}