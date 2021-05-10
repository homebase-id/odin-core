using System;
using System.Threading.Tasks;
using Refit;

namespace DotYou.Types
{
    public interface IAdminAuthenticationClient
    {
        private const string RootPath = "/api/authentication/admin";

        [Post(RootPath)]
        Task<ApiResponse<Guid>> Authenticate(string password);

        [Post(RootPath + "/extend")]
        Task<ApiResponse<NoResultResponse>> ExtendTokenLife(Guid token, int ttlSeconds);

        [Post(RootPath + "/expire")]
        Task<ApiResponse<NoResultResponse>> Expire(Guid token);

        [Get(RootPath)]
        Task<ApiResponse<bool>> IsValid(Guid token);
    }
}