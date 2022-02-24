using System.Security.Claims;

namespace Youverse.Core.Services.Authorization
{
    /// <summary>
    /// List of the Claim types used by YouFoundation
    /// </summary>
    public static class DotYouClaimTypes
    {
        public static string YouFoundationIssuer = "YouFoundation";

        public static string AppId = "https://schemas.youfoundation.id/2021/11/apps/AppId";
        
        public static string DeviceUid64 = "https://schemas.youfoundation.id/2021/11/apps/DeviceUid";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.youfoundation.id/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user with a valid certificate
        /// </summary>
        public static string IsIdentified = "https://schemas.youfoundation.id/2021/3/identity/IsIdentified";

        public static string SessionId = "https://schemas.youfoundation.id/2021/3/identity/SessionId";

        /// <summary>
        /// Specifies the identity performing actions on a given identity
        /// </summary>
        public static string Actor = "https://schemas.youfoundation.id/2021/3/identity/Actor";

        public static string PublicKeyCertificate = "https://schemas.youfoundation.id/2021/3/identity/PublicKeyCertificate";
        
        public static string AuthResult  = "https://schemas.youfoundation.id/2021/3/identity/AuthResult";
        
        public static string IsAuthorizedApp = "https://schemas.youfoundation.id/2021/3/identity/IsAuthorizedApp";
    }
}
