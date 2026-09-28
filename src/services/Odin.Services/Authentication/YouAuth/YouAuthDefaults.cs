
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
        /// Optional on the authorize request: which cipher seals the token response. Absent means
        /// <see cref="CipherAesCbc"/>, what every client got before the parameter existed. Echoed
        /// in every token response so a client can refuse a downgrade.
        /// </summary>
        public const string Cipher = "cipher";
        public const string CipherAesCbc = "aes-cbc";
        public const string CipherAesGcm = "aes-gcm";

        /// <summary>
        /// Where a relying party publishes what it calls itself and which paths are its callbacks.
        /// See docs/youauth-client-metadata-plan.md.
        /// </summary>
        public const string ClientMetadataPath = "/.well-known/youauth-client.json";

        /// <summary>
        /// Query parameter carrying a failure back to the relying party's redirect URI; the codes
        /// below are its values. The reference client reads it.
        /// </summary>
        public const string Error = "error";
        public const string ErrorDescription = "error_description";

        public const string ErrorInvalidRequest = "invalid-request";
        public const string ErrorAppRevoked = "app-revoked";
        public const string ErrorAccessDenied = "access-denied";
        public const string ErrorServerError = "server-error";

        /// <summary>
        /// Never sent by this server: the owner app's consent and app-registration pages send it
        /// when the owner declines. Recorded here so the vocabulary is in one place.
        /// </summary>
        public const string ErrorCancelledByUser = "cancelled-by-user";
    }
}