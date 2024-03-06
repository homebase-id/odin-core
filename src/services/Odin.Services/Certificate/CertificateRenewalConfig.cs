namespace Odin.Services.Certificate
{
    public class CertificateRenewalConfig
    {
        /// <summary>
        /// Specifies if the production servers of the certificate authority should be used.
        /// </summary>
        public bool UseCertificateAuthorityProductionServers { get; set; }

        /// <summary>
        /// The email addressed given to Certificate Authorities when users ask us to manage their certificates
        /// </summary>
        public string CertificateAuthorityAssociatedEmail { get; set; }
    }
}