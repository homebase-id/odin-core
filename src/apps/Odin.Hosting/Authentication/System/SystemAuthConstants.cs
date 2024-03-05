namespace Odin.Hosting.Authentication.System
{
    public static class SystemAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public const string SchemeName = "digital-identity-system";

        /// <summary>
        /// The name of the key used to store the token in cookies, dictionaries, etc.
        /// </summary>
        public static string Header = "SY4829";

    }
}