using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Configuration;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Version;

public class VersionApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<ApiResponse<VersionInfoResult>> GetVersionInfo()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IVersionTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.GetVersionInfo();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<TenantVersionInfo>> ForceVersionNumber(int version)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IVersionTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.ForceVersionNumber(version);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ForceVersionUpgrade()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IVersionTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.ForceVersionUpgrade();
            return apiResponse;
        }
    }
}