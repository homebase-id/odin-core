using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Core.Services.Authentication.Owner;

namespace Youverse.Hosting.Tests.AppAPI
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = "/api/apps/v1/auth";

        [Post(RootPath)]
        Task<ApiResponse<bool>> Authenticate([Body] AppDevice app);

        [Post(RootPath + "/extend")]
        Task<ApiResponse<NoResultResponse>> ExtendTokenLife(Guid token, int ttlSeconds);

        [Post(RootPath + "/expire")]
        Task<ApiResponse<NoResultResponse>> Expire(Guid token);

        [Get(RootPath)]
        Task<ApiResponse<bool>> IsValid(Guid token);

        [Get(RootPath + "/nonce")]
        Task<ApiResponse<ClientNoncePackage>> GenerateNonce();

        //TODO: move these to a secrets/provisioning controller

    }
}