using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.Apps
{
    public class AppAuthenticationService : IAppAuthenticationService
    {
        private readonly ExchangeGrantService _exchangeGrantService;

        public AppAuthenticationService(ExchangeGrantService exchangeGrantService)
        {
            _exchangeGrantService = exchangeGrantService;
        }

        public async Task<AppTokenValidationResult> ValidateClientToken(Guid accessRegistrationId)
        {
            //TODO: Move validation to exchange grant service
            var accessRegistration = await _exchangeGrantService.GetAccessRegistration(accessRegistrationId);
            var isValid = accessRegistration != null && accessRegistration.IsRevoked == false;

            if (isValid)
            {
                var exchangeGrant = await _exchangeGrantService.GetExchangeGrant(accessRegistration.GrantId);
                isValid = exchangeGrant != null && exchangeGrant.IsRevoked == false;
            }

            return new AppTokenValidationResult()
            {
                IsValid = isValid
            };
        }
    }
}