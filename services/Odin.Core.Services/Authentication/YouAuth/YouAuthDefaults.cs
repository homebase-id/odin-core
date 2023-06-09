using System.Security.Claims;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        public const string AuthorizationCode = "ac";
        public const string Initiator = "initiator";
        public const string ReturnUrl = "returnUrl";
        public const string Subject = "subject";
        
        public const string XTokenCookieName = "XT32";

        public const string IdentityClaim = ClaimTypes.Name; // SEB:TODO figure out the right claim
    }
}