namespace Youverse.Core.Services.Certificate.Renewal
{
    public class CertificateRenewalConfig
    {
        /// <summary>
        /// The number of times certificate validation should be checked before failing
        /// </summary>
        public int NumberOfCertificateValidationTries { get; set; }
        
        /// <summary>
        /// Specifies if the production servers of the certificate authority should be used.
        /// </summary>
        public bool UseCertificateAuthorityProductionServers { get; set; }

        /// <summary>
        /// The email addressed given to Certificate Authorities when users ask us to manage their certificates
        /// </summary>
        public string CertificateAuthorityAssociatedEmail { get; set; }

        //public CertificateSigningRequest CertificateSigningRequest { get; set; }

    }
}