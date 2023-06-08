using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Tests.AppAPI.ApiClient;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient.Security;

public class SecurityApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public SecurityApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _identity = identity;
        _ownerApi = ownerApi;
    }

    public async Task<RedactedOdinContext> GetSecurityContext()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse.Content;
        }
    }
}