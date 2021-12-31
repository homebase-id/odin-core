using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authentication.Owner
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// </summary>
    public interface IOwnerAuthenticationService
    {
        /// <summary>
        /// Authenticates the owner based on the <see cref="IPasswordReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <exception cref="YouverseSecurityException">Thrown when a user cannot be authenticated</exception>
        /// <returns></returns>
        Task<DotYouAuthenticationResult> Authenticate(IPasswordReply reply);

        /// <summary>
        /// Determines if the <paramref name="token"/> is valid and has not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <returns></returns>
        Task<bool> IsValidToken(Guid sessionToken);
        
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
        Task<NonceData> GenerateAuthenticationNonce();

        /// <summary>
        /// Returns the LoginKek used to access the primary and application data encryption keys
        /// </summary>
        /// <returns></returns>
        
        Task<SecureKey> GetMasterKey(Guid sessionToken, SecureKey rClientHalfKek);
    }
}