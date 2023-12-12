using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.AccountManagement;

public class OwnerAccountManagementApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public OwnerAccountManagementApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _identity = identity;
        _ownerApi = ownerApi;
    }

    public async Task<ApiResponse<RedactedOdinContext>> GetSecurityContext()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<DecryptedRecoveryKey>> GetAccountRecoveryKey()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.GetAccountRecoveryKey();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPassword(string currentPassword, string newPassword)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var request = new ResetPasswordRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword),
            NewPasswordReply = await _ownerApi.CalculatePasswordReply(authClient, newPassword)
        };

        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.ResetPassword(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey(string recoveryKey, string newPassword)
    {
        return await _ownerApi.ResetPasswordUsingRecoveryKey(this._identity.OdinId, recoveryKey, newPassword);
    }
    
    public async Task<ApiResponse<DeleteAccountResponse>> DeleteAccount(string currentPassword)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword),
        };
        
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.DeleteAccount(request);
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<DeleteAccountResponse>> UndeleteAccount(string currentPassword)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword),
        };
        
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.UndeleteAccount(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<AccountStatusResponse>> GetAccountStatus()
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.GetAccountStatus();
            return apiResponse;
        }
    }
}