using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Crypto;

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

    public async Task<ApiResponse<HttpContent>> ResetPassword(string currentPassword, string newPassword, OdinCryptoConfig odinCryptoConfig)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        var request = new ResetPasswordRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey, odinCryptoConfig),
            NewPasswordReply = await _ownerApi.CalculatePasswordReply(authClient, newPassword, clientEccFullKey, odinCryptoConfig)
        };

        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.ResetPassword(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey(string recoveryKey, string newPassword, OdinCryptoConfig odinCryptoConfig)
    {
        return await _ownerApi.ResetPasswordUsingRecoveryKey(this._identity.OdinId, recoveryKey, newPassword, odinCryptoConfig);
    }
    
    public async Task<ApiResponse<DeleteAccountResponse>> DeleteAccount(string currentPassword, OdinCryptoConfig odinCryptoConfig)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey, odinCryptoConfig),
        };
        
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerAccountManagement>(client, ownerSharedSecret);
            var apiResponse = await svc.DeleteAccount(request);
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<DeleteAccountResponse>> UndeleteAccount(string currentPassword, OdinCryptoConfig odinCryptoConfig)
    {
        using var authClient = _ownerApi.CreateAnonymousClient(_identity.OdinId);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var request = new DeleteAccountRequest()
        {
            CurrentAuthenticationPasswordReply = await _ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey, odinCryptoConfig),
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