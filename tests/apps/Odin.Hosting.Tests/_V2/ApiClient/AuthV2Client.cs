using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class AuthV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> VerifyToken()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IAuthHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.VerifyToken();
        return apiResponse;
    }

    public async Task<ApiResponse<string>> VerifySharedSecretEncryption(string checkValue)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IAuthHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.VerifySharedSecretEncryption(checkValue);
        return apiResponse;
    }
    
    public async Task<ApiResponse<string>> Logout()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IAuthHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.Logout();
        return apiResponse;
    }
    
}