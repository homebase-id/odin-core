namespace Odin.Hosting.Controllers.Home.Auth
{
    public static class HomeApiPathConstants
    {
        private const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";
        public const string HandleAuthorizationCodeCallbackMethodName = "auth-code-callback";
        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";
        
        public const string PingMethodName = "ping";
    }
}