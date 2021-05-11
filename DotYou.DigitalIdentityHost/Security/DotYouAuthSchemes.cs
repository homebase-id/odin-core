namespace DotYou.TenantHost.Security
{
    public static class DotYouAuthSchemes
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public const string DotIdentityOwner = "digital-identity-owner";

        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts. 
        /// </summary>
        //TODO: determine why I cannot use my own name here.  I must use 'certificate'
        public const string ExternalDigitalIdentityClientCertificate = "Certificate";
    }
}