using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.Home.Auth
{
    public static class HomeApiPathConstants
    {
        private const string BasePathV1 = GuestApiPathConstants.BuiltIn +"/home";
        public const string AuthV1 = BasePathV1 + "/auth";
        
        public const string HandleAuthorizationCodeCallbackMethodName = "auth-code-callback";
        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";
        public const string PingMethodName = "ping";
    }
}