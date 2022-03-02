using System;
using System.Security.Claims;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        public const string AuthorizationCode = "ac";
        public const string Initiator = "initiator";
        public const string ReturnUrl = "returnUrl";
        public const string Subject = "subject";

        public const string SessionCookieName = "EZ1921";

        public const string XTokenCookieName = "XT32";

        public const string IdentityClaim = ClaimTypes.Name; // SEB:TODO figure out the right claim

        public static readonly Guid AppId = Guid.Parse("10000000-0000-aaaa-1111-111222333444"); //TODO: should this be fixed here?
    }
}