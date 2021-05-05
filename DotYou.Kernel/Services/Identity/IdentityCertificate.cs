using DotYou.Types.Certificate;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DotYou.Kernel.Services.Identity
{
    /// <summary>
    /// An IdentityCertificate defines a certificate held by an individual human or organization.
    /// </summary>
    public sealed class IdentityCertificate
    {
        //private empty ctor handles deserialization
        private IdentityCertificate() { }

        public IdentityCertificate(Guid key, string domain, CertificateLocation location)
        {
            if (key == Guid.Empty)
            {
                throw new Exception("Guid must not be empty");
            }

            if (null == domain)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            if(null == location)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            Key = key;
            DomainName = domain;
            Location = location;

            SetCertificateInfo();
        }

        public Guid Key
        {
            get;
        }

        public string DomainName { get; }

        /// <summary>
        /// The Subject for the certificate
        /// </summary>
        public string CertificateSubject { get; private set; }

        /// <summary>
        /// A hexidecimal string of the Public Key
        /// </summary>
        public string CertificatePublicKeyString { get; private set; }
         
        /// <summary>
        /// The file location of the certificates
        /// </summary>
        public CertificateLocation Location { get; private set; }

        private void SetCertificateInfo()
        {
            using (var cert = this.LoadCertificate())
            {
                this.CertificatePublicKeyString = cert.GetPublicKeyString();
                this.CertificateSubject = cert.Subject;
            }
        }

        public X509Certificate2 LoadCertificate()
        {
            //_logger.LogDebug($"looking up cert for [{hostname}]");

            string certificatePath = this.Location.CertificatePath;
            string privateKeyPath = this.Location.PrivateKeyPath;

            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                throw new Exception($"No certificate configured for {this.DomainName}");
            }

            return CertificateLoader.LoadPublicPrivateRSAKey(certificatePath, privateKeyPath);

        }
    }
}