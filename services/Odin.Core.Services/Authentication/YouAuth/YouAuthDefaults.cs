using System;
using System.Security.Claims;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        public const string AuthorizationCode = "ac";
        public const string Initiator = "initiator";
        [Obsolete("SEB:TODO delete me")]
        public const string ReturnUrl = "returnUrl";
        public const string RedirectUri = "redirect_uri";
        public const string Subject = "subject";
        public const string SharedSecret = "ss64";
        
        public const string XTokenCookieName = "XT32";

        public const string IdentityClaim = ClaimTypes.Name; // SEB:TODO figure out the right claim
    }
}