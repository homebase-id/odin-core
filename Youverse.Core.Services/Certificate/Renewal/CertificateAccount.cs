namespace Youverse.Core.Services.Certificate.Renewal
{
    /// <summary>
    /// Account information used when creating or renewing SSL certificates
    /// </summary>
    public class CertificateAccount
    {
        /// <summary>
        /// Email address for the account
        /// </summary>
        public string Email { get; set; }
        
        /// <summary>
        /// Holds the key information for the account (i.e. in the case of LetsEncrypt this will be the Pem Key)
        /// </summary>
        public string AccountKey { get; set; }
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}