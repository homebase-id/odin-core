using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Authentication.Apps
{
    public class AppAuthenticationService : IAppAuthenticationService
    {
        private readonly IAppRegistrationService _appService;

        public AppAuthenticationService(IAppRegistrationService appService)
        {
            _appService = appService;
        }

        public async Task<AppTokenValidationResult> ValidateClientToken(Guid token)
        {
            var clientReg = await _appService.GetClientRegistration(token);
            var isValid = clientReg != null && clientReg.IsRevoked == false;
            
            // check the app
            if (isValid)
            {
                var appReg = await _appService.GetAppRegistration(clientReg.ApplicationId);
                isValid = appReg != null && appReg.IsRevoked == false;
            }
            
            return new AppTokenValidationResult()
            {
                IsValid = isValid
            };
        }
    }
}