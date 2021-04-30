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

    }
}