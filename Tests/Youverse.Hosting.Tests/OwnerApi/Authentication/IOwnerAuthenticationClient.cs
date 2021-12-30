using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Core.Services.Authentication.Owner;

namespace Youverse.Hosting.Tests.OwnerApi.Authentication
{
    public interface IOwnerAuthenticationClient
    {
        private const string RootPath = "/owner/api/v1/authentication";

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
        
        /// <summary>
        /// Creates a session for an app.  Use the resulting id to exchange for the session token
        /// </summary>
        /// <returns></returns>
        //NOTE: this targets the app authentication 
        [Post("/owner/api/v1/appauth/createappsession")]
        public Task<ApiResponse<Guid>> CreateAppSession(AppDevice appDevice);
    }
}