using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using DotYou.Types.Certificate;

namespace DotYou.IdentityRegistry
{
    /// <summary>
    /// An IdentityCertificate defines a certificate held by an individual human or organization.
    /// </summary>
    public sealed class IdentityCertificate
    {
        //private empty ctor handles deserialization
        private IdentityCertificate() { }

        public IdentityCertificate(Guid key, string domain, NameInfo owner, CertificateLocation location)
        {
            Guard.Argument(key, nameof(key)).NotEqual(Guid.Empty);
            Guard.Argument(domain, nameof(domain)).NotEmpty();
            Guard.Argument(location, nameof(location)).NotNull();

            Guard.Argument(location.CertificatePath, nameof(CertificateLocation.CertificatePath)).NotEmpty();
            Guard.Argument(location.PrivateKeyPath, nameof(CertificateLocation.PrivateKeyPath)).NotEmpty();
            
            //Guard.Argument(owner, nameof(owner)).NotNull();
            
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
        /// The file location of the certificates
        /// </summary>
        public CertificateLocation Location { get; private set; }

        private void SetCertificateInfo()
        {
            using (var cert = this.LoadCertificateWithPrivateKey())
            {
                this.CertificateSubject = cert.Subject;
            }
        }
        
        public X509Certificate2 LoadCertificateWithPrivateKey()
        {
            //_logger.LogDebug($"looking up cert for [{hostname}]");

            string certificatePath = this.Location.CertificatePath;
            string privateKeyPath = this.Location.PrivateKeyPath;

            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                return null;
            }
            
            // Console.WriteLine($"Public Key [{certificatePath}]");
            // Console.WriteLine($"Private Key [{privateKeyPath}]");

            return CertificateLoader.LoadPublicPrivateRSAKey(certificatePath, privateKeyPath);

        }
    }
}