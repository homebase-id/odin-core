using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Auth;

namespace Youverse.Hosting.Tests.OwnerApi.Authentication
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

        [Post(RootPath + "/todo_move_this")]
        Task<ApiResponse<NoResultResponse>> SetNewPassword([Body] PasswordReply reply);

        [Get(RootPath + "/getsalts")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNewSalts();
    }
}