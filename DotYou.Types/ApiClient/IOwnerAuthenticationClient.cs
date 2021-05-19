using System;
using System.Threading.Tasks;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IOwnerAuthenticationClient
    {
        private const string RootPath = "/api/admin/authentication";

        [Post(RootPath)]
        Task<ApiResponse<AuthenticationResult>> Authenticate([Body]NonceReplyPackage package);

        [Post(RootPath + "/extend")]
        Task<ApiResponse<NoResultResponse>> ExtendTokenLife(Guid token, int ttlSeconds);

        [Post(RootPath + "/expire")]
        Task<ApiResponse<NoResultResponse>> Expire(Guid token);

        [Get(RootPath)]
        Task<ApiResponse<bool>> IsValid(Guid token);

        [Get(RootPath + "/nonce")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNonce();
    }
}