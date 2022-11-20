using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Provisioning.Services.Certificate;

namespace Youverse.Provisioning
{
    public class ProvisioningConfig
    {
        private string _certificateRootPath;
        private string _dataRootPath;
        private string _certificateChallengeTokenPath;
        private string _serverPrivateKeyPath;
        private string _serverPublicKeyPath;

        public string ServerPublicKeyCertificatePath
        {
            get => _serverPublicKeyPath;
            set => _serverPublicKeyPath = PathUtil.OsIfy(value);
        }
        
        public string ServerPrivateKeyCertificatePath
        {
            get => _serverPrivateKeyPath;
            set => _serverPrivateKeyPath = PathUtil.OsIfy(value);
        }
        /// <summary>
        /// Path which contains the certificates for domains
        /// </summary>
        public string CertificateRootPath
        {
            get => _certificateRootPath;
            set => _certificateRootPath = PathUtil.OsIfy(value);
        }

        /// <summary>
        /// Path which contains the all data other than certificates (databases, images, etc.)
        /// </summary>
        public string DataRootPath
        {
            get => _dataRootPath;
            set => _dataRootPath = PathUtil.OsIfy(value);
        }

        /// <summary>
        /// Specifies the path where to write the token and key authorization
        /// used by the Certificate Challenge webhost when validating domain
        /// ownership as required by LetsEncrypt and potentially other
        /// Certificate Authorities)
        /// </summary>
        public string CertificateChallengeTokenPath
        {
            get => _certificateChallengeTokenPath;
            set => _certificateChallengeTokenPath = PathUtil.OsIfy(value);
        }

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

        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
        public List<string> AllowedOrigins { get; set; }

    }
}