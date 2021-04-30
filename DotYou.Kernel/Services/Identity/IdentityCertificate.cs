using System;

namespace Identity.Web.Certificate
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

            this.Key = key;
            this.DomainName = domain;
        }

        public Guid Key
        {
            get;
        }

        public string DomainName { get; }
        
        public CertificateLocation CertificateLocation { get; set; }
    }
}