namespace Youverse.Hosting.Controllers.YouAuth
{
    public static class YouAuthApiPathConstants
    {
        public const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";

        public const string ValidateAuthorizationCodeRequestMethodName= "validate-ac-req";
        public const string ValidateAuthorizationCodeRequestPath = AuthV1 + "/" + ValidateAuthorizationCodeRequestMethodName;
        
        public const string IsAuthenticatedMethodName =  "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";
    }
}