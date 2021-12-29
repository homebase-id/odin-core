using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

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
        /// Exchanges the 
        /// </summary>
        /// <param name="authCode"></param>
        /// <returns></returns>
        Task<DotYouAuthenticationResult> ExchangeAuthCode(Guid authCode);
        
        /// <summary>
        /// Determines if the app and device paired with its token are valid, not revoked, and have not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <returns></returns>
        Task<SessionValidationResult> ValidateSessionToken(Guid token);
        
        /// <summary>
        /// Extends the token life by <param name="ttlSeconds"></param> if it is valid.  Otherwise an <see cref="InvalidTokenException"/> is thrown
        /// </summary>
        /// <param name="token"></param>
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