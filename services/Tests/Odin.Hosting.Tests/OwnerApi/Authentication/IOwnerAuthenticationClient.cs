using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Cryptography;
using Odin.Core.Fluff;
using Odin.Core.Services.Authentication.Owner;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public interface IOwnerAuthenticationClient
    {
        private const string RootPath = OwnerApiPathConstants.AuthV1;

        [Post(RootPath)]
        Task<ApiResponse<OwnerAuthenticationResult>> Authenticate([Body] IPasswordReply package);

        [Post(RootPath + "/extend")]
        Task<ApiResponse<NoResultResponse>> ExtendTokenLife(Guid token, int ttlSeconds);

        [Post(RootPath + "/expire")]
        Task<ApiResponse<NoResultResponse>> Expire(Guid token);

        [Get(RootPath)]
        Task<ApiResponse<bool>> IsValid(Guid token);

        [Get(RootPath + "/nonce")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNonce();

        //TODO: move these to a secrets/provisioning controller

        [Post(RootPath + "/passwd")]
        Task<ApiResponse<NoResultResponse>> SetNewPassword([Body] PasswordReply reply);

        [Post(RootPath + "/resetpasswd")]
        Task<ApiResponse<HttpContent>> ResetPassword([Body] ResetPasswordRequest reply);
        
        [Get(RootPath + "/getsalts")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNewSalts();
    }
}