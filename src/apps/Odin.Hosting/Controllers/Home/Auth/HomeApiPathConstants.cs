using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.Home.Auth
{
    public static class HomeApiPathConstants
    {
        private const string BasePathV1 = GuestApiPathConstantsV1.BuiltIn +"/home";
        public const string AuthV1 = BasePathV1 + "/auth";
        public const string DataV1 = BasePathV1 + "/data";
        public const string CacheableV1 = DataV1 + "/cacheable";
        
        public const string HandleAuthorizationCodeCallbackMethodName = "auth-code-callback";
        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";
        public const string PingMethodName = "ping";
    }
}