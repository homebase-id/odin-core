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

        public IdentityCertificate(Guid key, string domain)
        {
            if (key == Guid.Empty)
            {
                throw new Exception("Guid must not be empty");
            }

            if (null == domain)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            Key = key;
            DomainName = domain;
        }

        public Guid Key
        {
            get;
        }

        public string DomainName { get; }

        public CertificateLocation Location { get; set; }

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