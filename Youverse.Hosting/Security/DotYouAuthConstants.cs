namespace Youverse.Hosting.Security
{
    public static class DotYouAuthConstants
    {

        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts. 
        /// </summary>
        //TODO: determine why I cannot use my own name here.  I must use 'certificate'
        public const string ExternalDigitalIdentityClientCertificateScheme = "Certificate";
        
        /// <summary>
        /// The name of the key used to store the token in cookies, dictionaries, etc.
        /// </summary>
        public static string TokenKey = "token";

    }
}