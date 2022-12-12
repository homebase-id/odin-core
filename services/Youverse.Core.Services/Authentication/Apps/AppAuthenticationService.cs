using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.Apps
{
    public class AppAuthenticationService : IAppAuthenticationService
    {

        public async Task<AppTokenValidationResult> ValidateClientToken(Guid accessRegistrationId)
        {
            throw new NotImplementedException("re-eval if we need this method");
        }
    }
}