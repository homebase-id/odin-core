using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Identity
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
        }

        public Guid Key
        {
            get;
        }

        public string DomainName { get; }

        /// <summary>
        /// The file location of the certificates
        /// </summary>
        public CertificateLocation Location { get; private set; }
    }
}