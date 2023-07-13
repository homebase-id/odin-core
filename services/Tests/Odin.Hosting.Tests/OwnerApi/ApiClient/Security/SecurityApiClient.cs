using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Security;

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

    public async Task<ApiResponse<DecryptedRecoveryKey>> GetAccountRecoveryKey()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetAccountRecoveryKey();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPassword(string recoveryKey, string newPassword)
    {
        return await _ownerApi.ResetPassword(this._identity.OdinId, recoveryKey, newPassword);
    }
}