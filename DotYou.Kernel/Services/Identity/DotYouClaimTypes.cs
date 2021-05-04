using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Identity
{
    /// <summary>
    /// List of the Claim types used by YouFoundation
    /// </summary>
    public class DotYouClaimTypes
    {
        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> owns this identity website
        /// </summary>
        public static string IsIdentityOwner = "https://schemas.youfoundation.id/2021/3/identity/IsIdentityOwner";

        /// <summary>
        /// Indicates the current <see cref="ClaimsPrincipal"/> is a user verified with a certificate
        /// </summary>
        public static string IsVerifiedUser = "https://schemas.youfoundation.id/2021/3/identity/IsVerifiedUser";

        /// <summary>
        /// Specifies the identity performing actions on a given identity
        /// </summary>
        public static string Actor = "https://schemas.youfoundation.id/2021/3/identity/Actor";
    }
}
