
namespace Odin.Services.Authentication.YouAuth
{
    public static class YouAuthDefaults
    {
        public const string XTokenCookieName = "XT32";
        
        public const string PublicKey = "public_key";
        public const string Salt = "salt";
        public const string State = "state";
        public const string Identity = "identity";

        /// <summary>
        /// Query parameter carrying a failure back to the relying party's redirect URI. The codes
        /// below are its values; the consent page's cancel and the home-site login use the same
        /// parameter, and the reference client reads it.
        /// </summary>
        public const string Error = "error";
        public const string ErrorDescription = "error_description";

        public const string ErrorInvalidRequest = "invalid-request";
        public const string ErrorAppRevoked = "app-revoked";
        public const string ErrorAccessDenied = "access-denied";
        public const string ErrorServerError = "server-error";
        public const string ErrorCancelledByUser = "cancelled-by-user";
    }
}