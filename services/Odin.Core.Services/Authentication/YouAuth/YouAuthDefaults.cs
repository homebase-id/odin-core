using System;
using System.Security.Claims;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        [Obsolete("SEB:TODO delete me")]
        public const string AuthorizationCode = "ac";
        [Obsolete("SEB:TODO delete me")]
        public const string Initiator = "initiator";
        [Obsolete("SEB:TODO delete me")]
        public const string ReturnUrl = "returnUrl";
        [Obsolete("SEB:TODO delete me")]
        public const string Subject = "subject";
        public const string SharedSecret = "ss64";
        
        public const string XTokenCookieName = "XT32";
        
        public const string Code = "code";
        public const string State = "state";
    }
}