using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Login;
using Odin.Core.Fluff;
using Odin.Services.Authentication.Owner;
using Odin.Services.EncryptionKeyService;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Services.Security;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public interface IOwnerAuthenticationClient
    {
        private const string RootPath = OwnerApiPathConstants.AuthV1;

        [Post(RootPath)]
        Task<ApiResponse<OwnerAuthenticationResult>> Authenticate([Body] PasswordReply package);

        [Post(RootPath + "/extend")]
        Task<ApiResponse<NoResultResponse>> ExtendTokenLife(Guid token, int ttlSeconds);

        [Post(RootPath + "/expire")]
        Task<ApiResponse<NoResultResponse>> Expire(Guid token);

        [Get(RootPath)]
        Task<ApiResponse<bool>> IsValid(Guid token);

        [Get(RootPath + "/nonce")]
        Task<ApiResponse<ClientNoncePackage>> GenerateAuthenticationNonce();

        //TODO: move these to a secrets/provisioning controller

        [Post(RootPath + "/passwd")]
        Task<ApiResponse<NoResultResponse>> SetNewPassword([Body] PasswordReply reply);

        [Post(RootPath + "/resetpasswdrk")]
        Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey([Body] ResetPasswordUsingRecoveryKeyRequest reply);

        [Post(RootPath + "/resetpasswd")]
        Task<ApiResponse<HttpContent>> ResetPassword([Body] ResetPasswordUsingRecoveryKeyRequest reply);

        [Get(RootPath + "/getsalts")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNewSalts();
        
        [Get(RootPath + "/publickey")]
        Task<ApiResponse<GetPublicKeyResponse>> GetPublicKey(PublicPrivateKeyType keyType);

        [Get(RootPath + "/publickey_ecc")]
        Task<ApiResponse<GetEccPublicKeyResponse>> GetPublicKeyEcc(PublicPrivateKeyType keyType);
    }
}