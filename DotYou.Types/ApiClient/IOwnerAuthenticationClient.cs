using System;
using System.Threading.Tasks;
using DotYou.Types.Cryptography;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IOwnerAuthenticationClient
    {
        private const string RootPath = "/api/admin/authentication";

        [Post(RootPath)]
        Task<ApiResponse<bool>> Authenticate([Body] IPasswordReply package);

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