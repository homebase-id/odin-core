using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.DataConversion;

public class UniversalDataConversionApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> PrepareIntroductionsRelease()
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalDataConversion>(client, ownerSharedSecret);
            var apiResponse = await svc.PrepareIntroductionsRelease();
            return apiResponse;
        }
    }
}