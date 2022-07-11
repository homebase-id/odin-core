using System.Security.Claims;

namespace Youverse.Core.Services.Authorization
{
    /// <summary>
    /// List of the Claim types used by YouFoundation
    /// </summary>
    public static class DotYouClaimTypes
    {
        public static string YouFoundationIssuer = "YouFoundation";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.youfoundation.id/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user with a valid certificate
        /// </summary>
        public static string IsIdentified = "https://schemas.youfoundation.id/2021/3/identity/IsIdentified";
        
        /// <summary>
        /// Indicates the caller is on the Youverse network
        /// </summary>
        public static string IsInNetwork = "https://schemas.youfoundation.id/2021/3/identity/IsInNetwork";

        public static string AuthResult  = "https://schemas.youfoundation.id/2021/3/identity/AuthResult";
        
        public static string IsAuthorizedApp = "https://schemas.youfoundation.id/2021/3/identity/IsAuthorizedApp";
    }
}
