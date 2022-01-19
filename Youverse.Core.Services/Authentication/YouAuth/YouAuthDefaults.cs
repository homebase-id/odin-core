using System.Security.Claims;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        public const string AuthorizationCode = "ac";
        public const string Initiator = "initiator";
        public const string ReturnUrl = "returnUrl";
        public const string Subject = "subject";

        public const string BasePath = "/api/owner/v1/youauth";  //TODO: this is a duplicate of the OwnerAPIConstants and needs to be fixed
        public const string CreateTokenFlowPath = BasePath + "/create-token-flow";
        public const string ValidateAuthorizationCodeRequestPath = BasePath + "/validate-ac-req";
        public const string ValidateAuthorizationCodeResponsePath = BasePath + "/validate-ac-res";
        public const string IsAuthenticated = BasePath + "/is-authenticated";
        public const string DeleteTokenPath = BasePath + "/delete-token";

        public const string CookieName = "EZ1921";

        public const string IdentityClaim = ClaimTypes.Name;  // SEB:TODO figure out the right claim
    }
}