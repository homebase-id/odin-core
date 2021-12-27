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
        public static string CookieName = "BX0900";

        /// <summary>
        /// The name of the key used to store the device id
        /// </summary>
        public static string DeviceUidCookieName = "dx03";

        /// <summary>
        /// The name of the key used to store the appId
        /// </summary>
        public static string AppIdCookieName = "yx04";
    }
}