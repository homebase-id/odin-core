using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Security;

public class SecurityApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<RedactedOdinContext> GetSecurityContext()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse.Content;
        }
    }

    public async Task<ApiResponse<DecryptedRecoveryKey>> GetAccountRecoveryKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetAccountRecoveryKey();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPassword(string currentPassword, string newPassword, OdinCryptoConfig odinCryptoConfig)
    {
        using var authClient = ownerApi.CreateAnonymousClient(identity.OdinId);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        var request = new ResetPasswordRequest()
        {
            CurrentAuthenticationPasswordReply =
                await ownerApi.CalculateAuthenticationPasswordReply(authClient, currentPassword, clientEccFullKey, odinCryptoConfig),
            NewPasswordReply = await ownerApi.CalculatePasswordReply(authClient, newPassword, clientEccFullKey, odinCryptoConfig)
        };

        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.ResetPassword(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey(string recoveryKey, string newPassword, OdinCryptoConfig odinCryptoConfig)
    {
        return await ownerApi.ResetPasswordUsingRecoveryKey(identity.OdinId, recoveryKey, newPassword, odinCryptoConfig);
    }

    public async Task<ApiResponse<HttpContent>> ConfigureShards(ConfigureShardsRequest request)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.ConfigureShards(request);
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<RemoteShardVerificationResult>> VerifyShards()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.VerifyShards();
            return apiResponse;
        }
    }
}