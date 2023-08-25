using System;

namespace Odin.Hosting.Controllers.Anonymous.Home
{
    public static class HomeApiPathConstants
    {
        public const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";
        public const string ValidateAuthorizationCodeRequestMethodName = "validate-ac-req";
        public const string ValidateAuthorizationCodeRequestPath = AuthV1 + "/" + ValidateAuthorizationCodeRequestMethodName;
        public const string FinalizeBridgeRequestMethodName = "finalize-bridge";
        public const string FinalizeBridgeRequestRequestPath = AuthV1 + "/" + FinalizeBridgeRequestMethodName;

        public const string HandleAuthorizationCodeCallbackMethodName = "auth-code-callback";
        
        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";
        
        public const string PingMethodName = "ping";

        
        public const string AuthorizationCode = "ac";
        public const string Initiator = "initiator";
        public const string ReturnUrl = "returnUrl";
        public const string Subject = "subject";
    }
}