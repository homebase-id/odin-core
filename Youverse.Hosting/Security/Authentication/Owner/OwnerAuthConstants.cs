namespace Youverse.Hosting.Security.Authentication.Owner
{
    public static class OwnerAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public const string DotIdentityOwnerScheme = "digital-identity-owner";

        /// <summary>
        /// The name of the key used to store the token in cookies, dictionaries, etc.
        /// </summary>
        public static string CookieName = "DY0810";

    }
}