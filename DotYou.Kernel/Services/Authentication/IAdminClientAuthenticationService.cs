using System;
using System.Threading.Tasks;
using DotYou.Types;

namespace DotYou.Kernel.Services.Authentication
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// </summary>
    public interface IAdminClientAuthenticationService
    {
        /// <summary>
        /// Authenticates a user for this <see cref="DotYouIdentity"/>.  Returns a token which can be later used
        /// to determine if the user is still authenticated.
        ///
        /// Note: for #prototrial, we are only using a password yet need to find a stronger methdod, such as certificate or otherwise.
        /// </summary>
        /// <param name="password"></param>
        /// <param name="ttlSeconds"></param>
        /// <returns></returns>
        Task<Guid> Authenticate(string password, int ttlSeconds);
        
        /// <summary>
        /// Determines if the <paramref name="token"/> is valid and has not expired.  
        /// </summary>
        /// <param name="token">The token to be validated</param>
        /// <param name="actorIdentifier">Out parameter populated with the identifier related to the token if it is valid</param>
        /// <returns></returns>
        Task<bool> IsAuthenticated(Guid token);
        
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
        void ExpireAuthenticationToken(Guid token);
    }
}