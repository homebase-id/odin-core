using System.Security.Claims;

namespace Odin.Services.Authorization
{
    /// <summary>
    /// List of the Claim types used by YouFoundation
    /// </summary>
    public static class OdinClaimTypes
    {
        public static string Issuer = "Odin";

        public static string YouFoundationIssuer = "Odin";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a backend system process
        /// </summary>
        public static string IsSystemProcess = "https://schemas.odin.earth/2021/3/identity/IsSystemProcess";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.odin.earth/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user with a valid certificate or has been authenticated via YouAuth
        /// </summary>
        public static string IsAuthenticated = "https://schemas.odin.earth/2021/3/identity/IsAuthenticated";
        
        public static string IsAuthorizedApp = "https://schemas.odin.earth/2021/3/identity/IsAuthorizedApp";
        
        public static string IsAuthorizedGuest = "https://schemas.odin.earth/2021/3/identity/IsAuthorizedGuest";

    }
}