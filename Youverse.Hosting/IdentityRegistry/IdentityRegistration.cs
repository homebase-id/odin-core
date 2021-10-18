using System;
using MessagePack;
using Youverse.Core.Util;

namespace Youverse.Hosting.IdentityRegistry
{
    [MessagePackObject]
    public class IdentityRegistration
    {
        private string _domainName;
        private Guid _domainKey;

        [Key(0)]
        public Guid Id { get; set; }

        /// <summary>
        /// A generated Guid based on the domain name
        /// </summary>
        [Key(1)]
        public Guid DomainKey
        {
            get { return _domainKey; }
            set
            {
                //no-op: this is set by the domain name field
            }
        }

        [Key(2)]
        public string DomainName
        {
            get => _domainName;
            set
            {
                _domainName = value;
                _domainKey = MiscUtils.MD5HashToGuid(value);
            }
        }

        [Key(3)]
        public string PrivateKeyRelativePath { get; set; }

        [Key(4)]
        public string PublicKeyCertificateRelativePath { get; set; }

        [Key(5)]
        public string FullChainCertificateRelativePath { get; set; }

        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        [Key(6)]
        public bool IsCertificateManaged { get; set; }
        
        [Key(7)]
        public CertificateRenewalInfo CertificateRenewalInfo { get; set; }
    }
}