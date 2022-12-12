using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Authentication.Apps;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    public interface IAppAuthenticationClient
    {
        private const string RootPath = "/api/apps/v1/auth";

        [Get(RootPath + "/validate")]
        public Task<ApiResponse<AppTokenValidationResult>> ValidateClientToken(string ssCat64);

    }
}