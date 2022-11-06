namespace Youverse.Provisioning.Services.Certificate
{
    public class CertificateOrder
    {
        public string Domain { get; set; }
        
        /// <summary>
        /// The email address to associate with the certificate authority.  If <see cref="UseBuiltInAccount"/> is true, this is ignored.
        /// </summary>
        public CertificateAccount Account { get; set; }
        
        /// <summary>
        /// Specifies we should use the built-in DotYou account for generating an SSL certificate.  If set to true, the email address is ignored 
        /// </summary>
        public bool UseBuiltInAccount { get; set; }
    }
}