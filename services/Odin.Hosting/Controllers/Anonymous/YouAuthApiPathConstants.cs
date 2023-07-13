using System;

namespace Odin.Hosting.Controllers.Anonymous
{
    public static class YouAuthApiPathConstants
    {
        public const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";

        [Obsolete("SEB:TODO delete me")]
        public const string ValidateAuthorizationCodeRequestMethodName = "validate-ac-req";
        [Obsolete("SEB:TODO delete me")]
        public const string ValidateAuthorizationCodeRequestPath = AuthV1 + "/" + ValidateAuthorizationCodeRequestMethodName;

        [Obsolete("SEB:TODO delete me")]
        public const string FinalizeBridgeRequestMethodName = "finalize-bridge";
        [Obsolete("SEB:TODO delete me")]
        public const string FinalizeBridgeRequestRequestPath = AuthV1 + "/" + FinalizeBridgeRequestMethodName;
        
        public const string RequestAuthorizationCodeMethodName = "authorize";
        public const string RequestAuthorizationCodePath = AuthV1 + "/" + RequestAuthorizationCodeMethodName;
        
        public const string RedeemCodeForTokenRequestMethodName = "token";
        public const string RedeemCodeForTokenCodeRequestPath = AuthV1 + "/" + RedeemCodeForTokenRequestMethodName;
        
        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token"; 

        public const string DrivesV1 = BasePathV1 + "/drive";
        public const string DriveReactionsV1 = DrivesV1 + "/files/reactions";

        public const string CirclesV1 = BasePathV1 + "/circles";
        public const string SecurityV1 = BasePathV1 + "/security";

        public const string CdnV1 = BasePathV1 + "/cdn";

        public const string PublicKeysV1 = BasePathV1 + "/public/keys";

        public const string PingMethodName = "ping";
    }
}