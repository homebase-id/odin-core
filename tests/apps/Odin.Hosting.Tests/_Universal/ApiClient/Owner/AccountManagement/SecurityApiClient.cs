using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Crypto;
using Odin.Services.Security;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

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

    public async Task<ApiResponse<RecoveryKeyResult>> GetAccountRecoveryKey()
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
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        var request = new ResetPasswordRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey),
            NewPasswordReply = await _ownerApi.CalculatePasswordReply(authClient, newPassword, clientEccFullKey)
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
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey),
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
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey),
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