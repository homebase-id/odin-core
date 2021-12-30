using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = "/api/apps/v1/auth";

        [Post(RootPath + "/exchangeCode")]
        public Task<ApiResponse<DotYouAuthenticationResult>> ExchangeAuthCode([Body] AuthCodeExchangeRequest request);

        [Post(RootPath + "/validate")]
        public Task<ApiResponse<object>> ValidateSessionToken(Guid sessionToken);

        [Post(RootPath + "/expire")]
        public Task<ApiResponse<SessionValidationResult>> ExpireSessionToken(Guid sessionToken);
    }
}