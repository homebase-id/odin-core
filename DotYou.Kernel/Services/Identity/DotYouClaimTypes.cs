﻿using System.Security.Claims;

namespace DotYou.Kernel.Services.Identity
{
    /// <summary>
    /// List of the Claim types used by YouFoundation
    /// </summary>
    public static class DotYouClaimTypes
    {
        public static string LoginKek = "https://schemas.youfoundation.id/2021/3/identity/LoginToken";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.youfoundation.id/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user with a valid certificate
        /// </summary>
        public static string IsIdentified = "https://schemas.youfoundation.id/2021/3/identity/IsIdentified";

        /// <summary>
        /// Specifies the identity performing actions on a given identity
        /// </summary>
        public static string Actor = "https://schemas.youfoundation.id/2021/3/identity/Actor";

        public static string PublicKeyCertificate = "https://schemas.youfoundation.id/2021/3/identity/PublicKeyCertificate";
    }
}
