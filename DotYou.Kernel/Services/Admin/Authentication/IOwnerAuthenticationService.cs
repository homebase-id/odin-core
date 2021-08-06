using System;
using System.Threading.Tasks;
using DotYou.Kernel.Cryptography;
using DotYou.Types;
using DotYou.Types.Cryptography;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// </summary>
    public interface IOwnerAuthenticationService
    {
        /// <summary>
        /// Authenticates the owner based on the <see cref="AuthenticationNonceReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <exception cref="AuthenticationException">Thrown when a user cannot be authenticated</exception>
        /// <returns></returns>
        Task<AuthenticationResult> Authenticate(AuthenticationNonceReply reply);

        /// <summary>
        /// Used for authenticating mobile apps and other mobile devices
        /// </summary>
        /// <param name="reply"></param>
        /// <returns></returns>
        Task<DeviceAuthenticationResult> AuthenticateDevice(AuthenticationNonceReply reply);

        /// <summary>
        /// Determines if the device <paramref name="token"/> is valid and has not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <returns></returns>
        Task<bool> IsValidDeviceToken(Guid token);
        
        /// <summary>
        /// Determines if the <paramref name="token"/> is valid and has not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <returns></returns>
        Task<bool> IsValidToken(Guid token);
        
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
        void ExpireToken(Guid token);
        
        /// <summary>
        /// Generates a one time value to used when authenticating a user
        /// </summary>
        /// <returns></returns>
        public Task<NonceData> GenerateAuthenticationNonce();

        public Task<bool> IsLoggedIn();
    }
}