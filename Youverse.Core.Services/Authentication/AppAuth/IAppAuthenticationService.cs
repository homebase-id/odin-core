using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// </summary>
    public interface IAppAuthenticationService
    {
        /// <summary>
        /// Creates a <see cref="DotYouAuthenticationResult"/> for the App and Device if they are both valid
        /// </summary>
        /// <exception cref="YouverseSecurityException">Thrown when a user cannot be authenticated</exception>
        /// <returns></returns>
        Task<Guid> CreateSessionToken(AppDevice appDevice);

        /// <summary>
        /// Exchanges the auth code provided from CreateSessionToken for a <see cref="DotYouAuthenticationResult"/>. 
        /// </summary>
        /// <returns></returns>
        Task<DotYouAuthenticationResult> ExchangeAuthCode(AuthCodeExchangeRequest request);
        
        /// <summary>
        /// Determines if the app and device paired with its token are valid, not revoked, and have not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <returns></returns>
        Task<SessionValidationResult> ValidateSessionToken(Guid token);
        
        /// <summary>
        /// Extends the token life by <param name="ttlSeconds"></param> if it is valid.
        /// </summary>
        Task ExtendTokenLife(Guid token, int ttlSeconds);

        /// <summary>
        /// Expires the <paramref name="token"/> thus making it invalid.  This can be used when a user
        /// clicks logout.  Invalid or expired tokens are ignored.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        void ExpireSession(Guid token);
        
    }
}