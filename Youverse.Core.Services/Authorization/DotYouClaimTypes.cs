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
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a backend system process
        /// </summary>
        public static string IsSystemProcess = "https://schemas.youfoundation.id/2021/3/identity/IsSystemProcess";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.youfoundation.id/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user with a valid certificate or has been authenticated via YouAuth
        /// </summary>
        public static string IsAuthenticated = "https://schemas.youfoundation.id/2021/3/identity/IsAuthenticated";

        public static string AuthResult = "https://schemas.youfoundation.id/2021/3/identity/AuthResult";

        public static string IsAuthorizedApp = "https://schemas.youfoundation.id/2021/3/identity/IsAuthorizedApp";
    }
}