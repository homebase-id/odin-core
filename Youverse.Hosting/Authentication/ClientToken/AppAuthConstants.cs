namespace Youverse.Hosting.Authentication.App
{
    public static class AppAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public const string SchemeName = "app-auth";

        /// <summary>
        /// The name of the key used to store the token in cookies.
        /// </summary>
        public static string ClientAuthTokenCookieName = "BX0900";

    }
}