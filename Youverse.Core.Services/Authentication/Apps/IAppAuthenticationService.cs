using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Authentication.Apps
{
    public interface IAppAuthenticationService
    {
        /// <summary>
        /// Validates the token given to a client during registration
        /// </summary>
        /// <param name="token"></param>
        Task<AppTokenValidationResult> ValidateClientToken(Guid token);
    }
}