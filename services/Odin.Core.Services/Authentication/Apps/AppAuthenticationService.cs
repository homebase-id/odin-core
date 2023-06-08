using System;
using System.Threading.Tasks;

namespace Odin.Core.Services.Authentication.Apps
{
    public class AppAuthenticationService : IAppAuthenticationService
    {
        public async Task<AppTokenValidationResult> ValidateClientToken(Guid accessRegistrationId)
        {
            await Task.CompletedTask;
            throw new NotImplementedException("re-eval if we need this method");
        }
    }
}